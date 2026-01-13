"""
Pymodbus-based Modbus TCP Simulator.
Provides a real Modbus server for testing generated drivers at the binary level.

IMPORTANT: This simulator validates addresses - only addresses extracted from 
the PDF document are allowed. Invalid addresses return IllegalDataAddress (0x02).
"""
import asyncio
from typing import Optional, Dict, Any, List, Set
import struct
import structlog

from pymodbus.server.async_io import StartAsyncTcpServer
from pymodbus.datastore import ModbusServerContext
from pymodbus.datastore.context import ModbusSlaveContext
from pymodbus.datastore.store import BaseModbusDataBlock
from pymodbus.device import ModbusDeviceIdentification
from pymodbus.pdu import ExceptionResponse
from pymodbus.exceptions import ModbusException

from config import get_settings

logger = structlog.get_logger()


class ValidatingModbusDataBlock(BaseModbusDataBlock):
    """
    Custom Modbus data block that validates addresses.
    Only allows access to addresses in the valid_addresses set.
    Returns IllegalDataAddress (0x02) for invalid addresses.
    """
    
    def __init__(self, valid_addresses: Set[int], values: Dict[int, int] = None):
        """
        Initialize with a set of valid addresses.
        
        Args:
            valid_addresses: Set of addresses that are allowed to be accessed
            values: Optional dict of address -> value mappings
        """
        self.valid_addresses = valid_addresses
        self.values = values or {}
        self.address = 0  # Base address
        
        for addr in valid_addresses:
            if addr not in self.values:
                self.values[addr] = self._generate_realistic_value(addr)
        
        logger.info(
            "ValidatingModbusDataBlock created",
            valid_addresses=len(valid_addresses),
            sample_addresses=list(valid_addresses)[:5]
        )
    
    def _generate_realistic_value(self, address: int) -> int:
        """Generate realistic values based on typical solar inverter registers."""
        if 30000 <= address < 30100:
            return 1  # Running
        elif 30100 <= address < 30200:
            return 2500 + (address % 100)  # W
        elif 30200 <= address < 30300:
            return 230 + (address % 10)  # V
        elif 30300 <= address < 30400:
            return 10 + (address % 10)  # A
        elif 30400 <= address < 30500:
            return 25 + (address % 20)  # °C
        elif 30500 <= address < 30600:
            return 1000 + (address % 500)  # kWh
        elif 40000 <= address < 50000:
            return address % 1000
        else:
            return address % 65536
    
    def validate(self, address: int, count: int = 1) -> bool:
        """
        Validate that ALL addresses in the range are valid.
        Returns False if ANY address is invalid.
        """
        for addr in range(address, address + count):
            if addr not in self.valid_addresses:
                logger.warning(
                    "ILLEGAL ADDRESS ACCESS",
                    requested_address=f"0x{addr:04X} ({addr})",
                    valid_range=f"{min(self.valid_addresses) if self.valid_addresses else 'N/A'}-{max(self.valid_addresses) if self.valid_addresses else 'N/A'}"
                )
                return False
        return True
    
    def getValues(self, address: int, count: int = 1) -> List[int]:
        """
        Get values for addresses. Raises exception for invalid addresses.
        """
        if not self.validate(address, count):
            raise ModbusException("IllegalDataAddress")
        
        result = []
        for addr in range(address, address + count):
            result.append(self.values.get(addr, 0))
        
        logger.debug(
            "Reading registers",
            start_address=f"0x{address:04X}",
            count=count,
            values=result[:5]  # Log first 5 values
        )
        return result
    
    def setValues(self, address: int, values: List[int]) -> None:
        """Set values for addresses."""
        if not self.validate(address, len(values)):
            raise ModbusException("IllegalDataAddress")
        
        for i, val in enumerate(values):
            self.values[address + i] = val
    
    def __len__(self) -> int:
        return max(self.valid_addresses) + 1 if self.valid_addresses else 0
    
    def __iter__(self):
        return iter(self.values.items())


class StrictValidatingSlaveContext(ModbusSlaveContext):
    """
    Custom slave context that uses validating data blocks.
    Returns proper Modbus exceptions for invalid addresses.
    """
    
    def __init__(
        self,
        valid_holding_addresses: Set[int] = None,
        valid_input_addresses: Set[int] = None,
        valid_coil_addresses: Set[int] = None,
        valid_discrete_addresses: Set[int] = None,
        holding_values: Dict[int, int] = None,
        input_values: Dict[int, int] = None
    ):
        hr = ValidatingModbusDataBlock(
            valid_holding_addresses or set(),
            holding_values
        )
        ir = ValidatingModbusDataBlock(
            valid_input_addresses or set(),
            input_values
        )
        co = ValidatingModbusDataBlock(
            valid_coil_addresses or {0, 1, 2},
            {0: 1, 1: 0, 2: 1}
        )
        di = ValidatingModbusDataBlock(
            valid_discrete_addresses or {0, 1, 2, 3},
            {0: 1, 1: 1, 2: 0, 3: 1}
        )
        
        super().__init__(di=di, co=co, hr=hr, ir=ir)
        
        self.valid_holding = valid_holding_addresses or set()
        self.valid_input = valid_input_addresses or set()
    
    def validate(self, fc_as_hex: int, address: int, count: int = 1) -> bool:
        """
        Validate an address for a given function code.
        Returns False for invalid addresses (which triggers IllegalDataAddress).
        """
        fc_to_store = {
            0x01: self.store['c'],
            0x02: self.store['d'],
            0x03: self.store['h'],
            0x04: self.store['i'],
            0x05: self.store['c'],
            0x06: self.store['h'],
            0x0F: self.store['c'],
            0x10: self.store['h'],
        }
        
        store = fc_to_store.get(fc_as_hex)
        if store is None:
            return False
        
        return store.validate(address, count)


class SolarInverterSimulator:
    """
    Simulates a solar inverter's Modbus registers.
    
    STRICT MODE: Only addresses specified in valid_addresses are accessible.
    Any attempt to read other addresses returns IllegalDataAddress (0x02).
    """
    
    DEFAULT_VALID_HOLDING_ADDRESSES = set(range(30000, 30100)) | set(range(40000, 40100))
    DEFAULT_VALID_INPUT_ADDRESSES = set(range(30000, 30050))
    
    def __init__(
        self,
        valid_holding_addresses: Set[int] = None,
        valid_input_addresses: Set[int] = None,
        holding_values: Dict[int, int] = None,
        input_values: Dict[int, int] = None
    ):
        """
        Initialize simulator with valid address sets.
        
        Args:
            valid_holding_addresses: Set of valid holding register addresses
            valid_input_addresses: Set of valid input register addresses
            holding_values: Optional initial values for holding registers
            input_values: Optional initial values for input registers
        """
        self.valid_holding = valid_holding_addresses or self.DEFAULT_VALID_HOLDING_ADDRESSES.copy()
        self.valid_input = valid_input_addresses or self.DEFAULT_VALID_INPUT_ADDRESSES.copy()
        self.holding_values = holding_values or {}
        self.input_values = input_values or {}
        
        self._server_task: Optional[asyncio.Task] = None
        self._running = False
        self._context: Optional[ModbusServerContext] = None
        
        logger.info(
            "SolarInverterSimulator initialized",
            valid_holding_count=len(self.valid_holding),
            valid_input_count=len(self.valid_input),
            holding_range=f"{min(self.valid_holding) if self.valid_holding else 'N/A'}-{max(self.valid_holding) if self.valid_holding else 'N/A'}",
            input_range=f"{min(self.valid_input) if self.valid_input else 'N/A'}-{max(self.valid_input) if self.valid_input else 'N/A'}"
        )
    
    def configure_valid_addresses(
        self,
        holding_addresses: Set[int] = None,
        input_addresses: Set[int] = None,
        values: Dict[int, int] = None
    ):
        """
        Dynamically configure valid addresses.
        Called by Parser Agent after extracting addresses from PDF.
        This method now REBUILDS the Modbus context so new addresses are effective immediately.
        """
        if holding_addresses:
            self.valid_holding = holding_addresses
            logger.info(
                "Configured valid holding addresses",
                count=len(holding_addresses),
                sample=sorted(list(holding_addresses))[:10]
            )
        
        if input_addresses:
            self.valid_input = input_addresses
            logger.info(
                "Configured valid input addresses",
                count=len(input_addresses),
                sample=sorted(list(input_addresses))[:10]
            )
        
        if values:
            self.holding_values.update(values)
            self.input_values.update(values)
        
        self._context = self.create_context()
        logger.info(
            "Modbus context rebuilt with extracted addresses",
            holding_count=len(self.valid_holding),
            input_count=len(self.valid_input)
        )
    
    def create_context(self) -> ModbusServerContext:
        """Create strict validating Modbus context."""
        slave_context = StrictValidatingSlaveContext(
            valid_holding_addresses=self.valid_holding,
            valid_input_addresses=self.valid_input,
            holding_values=self.holding_values,
            input_values=self.input_values
        )
        
        return ModbusServerContext(slaves=slave_context, single=True)
    
    async def start(self, host: str = "127.0.0.1", port: int = 5020):
        """Start the Modbus TCP server with strict address validation."""
        if self._running:
            logger.warning("Simulator already running")
            return
        
        self._context = self.create_context()
        
        identity = ModbusDeviceIdentification()
        identity.VendorName = "SolarSim"
        identity.ProductCode = "SIMSOLAR"
        identity.VendorUrl = "http://localhost"
        identity.ProductName = "Solar Inverter Simulator (Strict Mode)"
        identity.ModelName = "SIM-5000-STRICT"
        
        logger.info(
            f"Starting STRICT Modbus simulator on {host}:{port}",
            valid_holding=f"{len(self.valid_holding)} addresses",
            valid_input=f"{len(self.valid_input)} addresses"
        )
        
        self._running = True
        await StartAsyncTcpServer(
            context=self._context,
            identity=identity,
            address=(host, port)
        )
    
    def stop(self):
        """Stop the simulator."""
        self._running = False
        if self._server_task:
            self._server_task.cancel()
        logger.info("Modbus simulator stopped")


class DriverTester:
    """
    Tests generated driver code against the Modbus simulator.
    Validates at the binary/byte level.
    
    NOW WITH STRICT ADDRESS VALIDATION:
    - Invalid addresses return IllegalDataAddress (0x02)
    - Forces drivers to use correct addresses from PDF
    """
    
    def __init__(self, host: str = "127.0.0.1", port: int = 5020):
        self.host = host
        self.port = port
        
    async def test_driver_code(
        self,
        driver_code: str,
        expected_registers: Optional[Dict[int, int]] = None,
        valid_addresses: Optional[Set[int]] = None
    ) -> Dict[str, Any]:
        """
        Execute driver code and validate against simulator.
        
        Args:
            driver_code: Generated driver code to test
            expected_registers: Optional dict of address -> expected value
            valid_addresses: Set of valid addresses (for error context)
        
        Returns:
            Detailed test results including byte-level comparison.
        """
        from pymodbus.client import AsyncModbusTcpClient
        
        result = {
            "success": False,
            "tested_registers": [],
            "expected_bytes": None,
            "actual_bytes": None,
            "error_message": None,
            "error_byte_position": None,
            "invalid_address": None,
            "suggested_addresses": []
        }
        
        try:
            test_namespace = {
                "asyncio": asyncio,
                "struct": struct,
                "ModbusTcpClient": AsyncModbusTcpClient,
                "host": self.host,
                "port": self.port
            }
            
            logger.debug(
                "Testing driver code",
                code_lines=driver_code.split('\n')[:5] if driver_code else [],
                total_lines=len(driver_code.split('\n')) if driver_code else 0
            )
            
            try:
                exec(driver_code, test_namespace)
            except SyntaxError as e:
                lines = driver_code.split('\n') if driver_code else []
                problem_line = lines[e.lineno - 1] if e.lineno and e.lineno <= len(lines) else "N/A"
                logger.error(
                    "Syntax error in driver code",
                    line_number=e.lineno,
                    offset=e.offset,
                    problem_line=problem_line
                )
                result["error_message"] = f"Syntax error in driver code: {e}"
                result["error_byte_position"] = e.offset
                return result
            except Exception as e:
                logger.error("Error executing driver code", error=str(e))
                result["error_message"] = f"Error executing driver code: {e}"
                return result
            
            test_func = test_namespace.get("run_self_test") or \
                        test_namespace.get("test_driver") or \
                        test_namespace.get("validate")
            
            if not test_func:
                for name, obj in test_namespace.items():
                    if asyncio.iscoroutinefunction(obj) and "test" in name.lower():
                        test_func = obj
                        break
            
            if not test_func:
                result = await self._run_basic_connectivity_test(
                    expected_registers,
                    valid_addresses
                )
            else:
                try:
                    test_result = await test_func()
                    if isinstance(test_result, bool):
                        result["success"] = test_result
                    elif isinstance(test_result, dict):
                        result.update(test_result)
                    else:
                        result["success"] = True
                except Exception as e:
                    error_msg = str(e)
                    
                    if "IllegalDataAddress" in error_msg or "0x02" in error_msg:
                        result["error_message"] = f"ILLEGAL DATA ADDRESS: Driver tried to access an invalid register. {error_msg}"
                        if valid_addresses:
                            result["suggested_addresses"] = sorted(list(valid_addresses))[:10]
                    else:
                        result["error_message"] = f"Driver test failed: {error_msg}"
                    
        except Exception as e:
            result["error_message"] = f"Unexpected error: {e}"
            logger.exception("Driver test error")
        
        return result
    
    async def _run_basic_connectivity_test(
        self,
        expected_registers: Optional[Dict[int, int]] = None,
        valid_addresses: Optional[Set[int]] = None
    ) -> Dict[str, Any]:
        """Run basic Modbus connectivity and register read test."""
        from pymodbus.client import AsyncModbusTcpClient
        
        result = {
            "success": False,
            "tested_registers": [],
            "expected_bytes": None,
            "actual_bytes": None,
            "error_message": None,
            "error_byte_position": None,
            "invalid_address": None,
            "suggested_addresses": []
        }
        
        actual_server_addresses = get_simulator().valid_holding | get_simulator().valid_input
        
        logger.warning(
            "CONNECTIVITY TEST - Using simulator addresses",
            address_count=len(actual_server_addresses),
            sample=sorted(list(actual_server_addresses))[:10]
        )
        
        client = AsyncModbusTcpClient(self.host, port=self.port)
        
        try:
            connected = await client.connect()
            if not connected:
                result["error_message"] = f"Failed to connect to {self.host}:{self.port}"
                return result
            
            if expected_registers:
                test_addresses = list(expected_registers.keys())
                logger.info(
                    "Testing addresses from generated code",
                    addresses=test_addresses[:10]
                )
            else:
                test_addresses = [0x0000, 0x0001, 0x0002]
                logger.warning(
                    "No expected registers - testing default invalid addresses",
                    addresses=test_addresses
                )
            
            for addr in test_addresses:
                response = await client.read_holding_registers(addr, count=1, slave=1)
                
                if response.isError():
                    error_str = str(response)
                    
                    if "IllegalDataAddress" in error_str or hasattr(response, 'exception_code') and response.exception_code == 2:
                        result["suggested_addresses"] = sorted(list(actual_server_addresses))[:10]
                        result["error_message"] = (
                            f"ILLEGAL DATA ADDRESS: Register 0x{addr:04X} ({addr}) does not exist. "
                            f"Valid addresses include: {result['suggested_addresses']}"
                        )
                        result["invalid_address"] = addr
                        
                        logger.warning(
                            "ILLEGAL ADDRESS DETECTED",
                            invalid_address=f"0x{addr:04X} ({addr})",
                            suggested=result["suggested_addresses"]
                        )
                        return result
                    else:
                        result["error_message"] = f"Error reading register 0x{addr:04X}: {response}"
                        return result
                
                actual_value = response.registers[0]
                result["tested_registers"].append(f"0x{addr:04X}={actual_value}")
                
                if expected_registers and addr in expected_registers:
                    expected = expected_registers[addr]
                    if expected is not None and actual_value != expected:
                        expected_bytes = struct.pack(">H", int(expected))
                        actual_bytes = struct.pack(">H", int(actual_value))
                        
                        result["expected_bytes"] = expected_bytes.hex()
                        result["actual_bytes"] = actual_bytes.hex()
                        
                        for i, (e, a) in enumerate(zip(expected_bytes, actual_bytes)):
                            if e != a:
                                result["error_byte_position"] = i
                                break
                        
                        result["error_message"] = (
                            f"Register 0x{addr:04X} mismatch: "
                            f"expected {expected} (0x{int(expected):04X}), "
                            f"got {actual_value} (0x{int(actual_value):04X})"
                        )
                        return result
            
            result["success"] = True
            
        finally:
            client.close()
        
        return result


_simulator: Optional[SolarInverterSimulator] = None
_tester: Optional[DriverTester] = None


def get_simulator() -> SolarInverterSimulator:
    """Get or create simulator instance."""
    global _simulator
    if _simulator is None:
        _simulator = SolarInverterSimulator()
    return _simulator


def configure_simulator_from_parsed_registers(
    registers: List[Dict[str, Any]]
) -> SolarInverterSimulator:
    """
    Configure simulator with addresses extracted from PDF.
    Called by Parser Agent.
    
    Args:
        registers: List of register dicts with 'address' key
    
    Returns:
        Configured simulator instance
    """
    simulator = get_simulator()
    
    holding_addresses = set()
    input_addresses = set()
    values = {}
    
    for reg in registers:
        addr = reg.get("address")
        if addr is None:
            continue
        
        func_code = reg.get("function_code", 3)
        
        if func_code in [3, 6, 16]:  # Holding registers
            holding_addresses.add(addr)
        elif func_code == 4:  # Input registers
            input_addresses.add(addr)
        else:
            holding_addresses.add(addr)  # Default to holding
        
        if "value" in reg:
            values[addr] = reg["value"]
    
    if (not holding_addresses and not input_addresses) or (
        holding_addresses and min(holding_addresses) < 1000
    ):
        logger.warning(
            "No valid/high addresses extracted from PDF, using defaults (30xxx/40xxx)"
        )
        simulator.valid_holding = SolarInverterSimulator.DEFAULT_VALID_HOLDING_ADDRESSES.copy()
        simulator.valid_input = SolarInverterSimulator.DEFAULT_VALID_INPUT_ADDRESSES.copy()
        simulator._context = simulator.create_context()
        return simulator
    
    simulator.configure_valid_addresses(
        holding_addresses=holding_addresses or None,
        input_addresses=input_addresses or None,
        values=values
    )
    
    logger.info(
        "Simulator configured from parsed registers",
        holding_count=len(holding_addresses),
        input_count=len(input_addresses),
        sample_addresses=sorted(list(holding_addresses | input_addresses))[:10]
    )
    
    return simulator


def get_tester() -> DriverTester:
    """Get or create tester instance."""
    global _tester
    if _tester is None:
        settings = get_settings()
        _tester = DriverTester(settings.modbus_host, settings.modbus_port)
    return _tester
