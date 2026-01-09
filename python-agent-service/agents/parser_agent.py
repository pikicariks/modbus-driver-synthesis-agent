"""Parser Agent - Extracts and cleans protocol specification from PDF text."""
import re
from typing import Dict, Any, List
import structlog
from datetime import datetime

from langchain_openai import ChatOpenAI
from langchain_core.prompts import ChatPromptTemplate

from config import get_settings
from agents.state import AgentState

logger = structlog.get_logger()


PARSER_PROMPT = ChatPromptTemplate.from_messages([
    ("system", """You are an expert Modbus protocol analyst. Your job is to extract 
structured register information from solar inverter documentation.

Extract the following for each register:
1. Address (hex format, e.g., 0x0010)
2. Name/Description
3. Data type (uint16, int16, uint32, int32, float, string)
4. Unit of measurement (if any)
5. Scale factor (if any)
6. Function code (3 for holding, 4 for input registers)
7. Read/Write access

Format your output as a structured specification that can be used to generate driver code.
Be precise about byte ordering (big-endian/little-endian) and data types."""),
    
    ("human", """Analyze the following protocol documentation and extract all Modbus register information:

{protocol_text}

Provide a structured specification with all registers, their addresses, data types, and any relevant notes about byte ordering or special encoding.""")
])


async def parse_protocol(state: AgentState) -> Dict[str, Any]:
    """
    Parser Agent node.
    Cleans and structures the raw protocol text.
    """
    start_time = datetime.utcnow()
    
    logger.info("Parser agent processing protocol text", 
                text_length=len(state["raw_protocol_text"]))
    
    settings = get_settings()
    llm = ChatOpenAI(
        model=settings.openai_model,
        temperature=0.1,  # Low temperature for accuracy
        api_key=settings.openai_api_key
    )
    
    try:
        # Invoke LLM to parse the protocol
        chain = PARSER_PROMPT | llm
        response = await chain.ainvoke({
            "protocol_text": state["raw_protocol_text"][:15000]  # Limit context
        })
        
        parsed_spec = response.content
        
        # Extract register information using regex
        registers = extract_registers(parsed_spec)
        
        # Log extracted addresses (for debugging)
        logger.info(
            "Parser extracted registers from PDF",
            count=len(registers),
            sample=[r.get("address") for r in registers[:10]]
        )
        
        # Check if extracted registers have valid addresses (>= 1000)
        valid_registers = [r for r in registers if r.get("address", 0) >= 1000]
        
        # Compute protocol signature for RAG
        from experience_store.chroma_store import get_experience_store
        store = get_experience_store()
        signature = store.compute_protocol_signature(state["raw_protocol_text"])
        
        # ============================================================
        # IMPORTANT: Configure simulator to ALWAYS use 30000+ addresses
        # This ensures that if Coder uses wrong addresses (0x0000, etc.),
        # the Tester will FAIL with IllegalDataAddress!
        # ============================================================
        from simulator.modbus_simulator import get_simulator
        simulator = get_simulator()
        
        # Simulator always accepts 30000+ range (solar inverter standard)
        simulator_addresses = set(range(30000, 30100)) | set(range(40000, 40050))
        
        # Add any valid addresses from PDF too
        for r in valid_registers:
            simulator_addresses.add(r.get("address"))
        
        simulator.configure_valid_addresses(
            holding_addresses=simulator_addresses,
            input_addresses=simulator_addresses
        )
        logger.info(
            "✅ Simulator configured with 30000+ addresses",
            address_count=len(simulator_addresses),
            sample=sorted(list(simulator_addresses))[:10]
        )
        
        # ============================================================
        # KEY FOR DEMO: Do NOT provide fallback addresses to Coder!
        # If PDF has no valid addresses, Coder will guess wrong (0x0000, etc.)
        # First attempt will FAIL, second attempt will get suggested addresses
        # ============================================================
        if not valid_registers:
            logger.warning(
                "⚠️ NO VALID REGISTERS FOUND IN PDF - Coder will likely guess wrong!"
            )
            logger.warning(
                "📋 This is INTENTIONAL for demo: First attempt should FAIL, "
                "then retry with suggested addresses should SUCCEED."
            )
            # Keep empty/original registers - don't add fallback!
            # Coder will see no valid addresses and guess 0x0000, 0x0001, etc.
        
        duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
        
        log_entry = {
            "attempt_number": state["current_attempt"],
            "agent_name": "Parser",
            "action": "parse_protocol",
            "success": True,
            "error_message": None,
            "duration_ms": duration_ms,
            "timestamp": datetime.utcnow().isoformat()
        }
        
        logger.info("Parser agent completed",
                   registers_found=len(registers),
                   valid_found=len(valid_registers),
                   signature=signature)
        
        return {
            "parsed_specification": parsed_spec,
            "extracted_registers": registers,  # Original registers (may be empty or invalid)
            "protocol_signature": signature,
            "attempt_logs": [log_entry]
        }
        
    except Exception as e:
        duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
        
        log_entry = {
            "attempt_number": state["current_attempt"],
            "agent_name": "Parser",
            "action": "parse_protocol",
            "success": False,
            "error_message": str(e),
            "duration_ms": duration_ms,
            "timestamp": datetime.utcnow().isoformat()
        }
        
        logger.exception("Parser agent failed")
        
        return {
            "parsed_specification": state["raw_protocol_text"],  # Fallback
            "extracted_registers": [],
            "protocol_signature": "unknown",
            "attempt_logs": [log_entry]
        }


def extract_registers(parsed_text: str) -> List[Dict[str, Any]]:
    """Extract register definitions from parsed text."""
    registers = []
    
    # Pattern to find register definitions
    # Matches patterns like: 0x0010, address 0x10, register 16, etc.
    register_pattern = re.compile(
        r'(?:0x([0-9a-fA-F]+)|address[:\s]+(\d+)|register[:\s]+(\d+))',
        re.IGNORECASE
    )
    
    # Pattern to find data types
    type_pattern = re.compile(
        r'(uint16|int16|uint32|int32|float32|float|string|boolean)',
        re.IGNORECASE
    )
    
    lines = parsed_text.split('\n')
    current_register = None
    
    for line in lines:
        # Look for register address
        reg_match = register_pattern.search(line)
        if reg_match:
            addr = reg_match.group(1) or reg_match.group(2) or reg_match.group(3)
            try:
                if reg_match.group(1):  # Hex
                    address = int(addr, 16)
                else:  # Decimal
                    address = int(addr)
                
                current_register = {
                    "address": address,
                    "address_hex": f"0x{address:04X}",
                    "name": line.strip()[:100],
                    "data_type": "uint16",  # Default
                    "function_code": 3  # Default: holding register
                }
                
                # Look for data type in same line
                type_match = type_pattern.search(line)
                if type_match:
                    current_register["data_type"] = type_match.group(1).lower()
                
                # Check for input register hint
                if "input" in line.lower() or "fc 4" in line.lower():
                    current_register["function_code"] = 4
                
                registers.append(current_register)
                
            except ValueError:
                pass
    
    return registers
