"""Tester Agent - Validates driver against Pymodbus simulator."""
from typing import Dict, Any
import structlog
from datetime import datetime

from config import get_settings
from agents.state import AgentState
from simulator.modbus_simulator import get_tester

logger = structlog.get_logger()


async def test_driver(state: AgentState) -> Dict[str, Any]:
    """
    Tester Agent node.
    Executes generated driver code against Pymodbus simulator.
    Validates at the binary/byte level.
    """
    start_time = datetime.utcnow()
    
    logger.info("Tester agent validating driver",
               attempt=state["current_attempt"],
               code_length=len(state.get("generated_code", "") or ""))
    
    if not state.get("generated_code"):
        log_entry = {
            "attempt_number": state["current_attempt"],
            "agent_name": "Tester",
            "action": "test_driver",
            "success": False,
            "error_message": "No code to test",
            "duration_ms": 0,
            "timestamp": datetime.utcnow().isoformat()
        }
        
        return {
            "test_passed": False,
            "test_error_message": "No code to test",
            "test_error_bytes": None,
            "test_byte_position": None,
            "tested_registers": [],
            "invalid_address": None,
            "suggested_addresses": [],
            "attempt_logs": [log_entry]
        }
    
    try:
        tester = get_tester()
        
        expected_registers = {}
        pdf_addresses = set()
        
        for reg in state.get("extracted_registers", []):
            if "address" in reg:
                addr = reg["address"]
                pdf_addresses.add(addr)
                expected_registers[addr] = None
        
        logger.info(
            "PDF extracted addresses (what code will use)",
            extracted_count=len(pdf_addresses),
            sample=sorted(list(pdf_addresses))[:10]
        )
        
        from simulator.modbus_simulator import get_simulator
        simulator = get_simulator()
        simulator_valid_addresses = simulator.valid_holding | simulator.valid_input
        
        logger.warning(
            "SIMULATOR valid addresses (what will actually work)",
            address_count=len(simulator_valid_addresses),
            sample=sorted(list(simulator_valid_addresses))[:10]
        )
        
        result = await tester.test_driver_code(
            state["generated_code"],
            expected_registers if expected_registers else None,
            simulator_valid_addresses  # For error context / suggestions only
        )
        
        duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
        
        log_entry = {
            "attempt_number": state["current_attempt"],
            "agent_name": "Tester",
            "action": "test_driver",
            "success": result["success"],
            "error_message": result.get("error_message"),
            "duration_ms": duration_ms,
            "timestamp": datetime.utcnow().isoformat()
        }
        
        if result["success"]:
            logger.info("Tester agent: Driver passed validation",
                       tested_registers=result.get("tested_registers", []))
        else:
            logger.warning("Tester agent: Driver failed validation",
                          error=result.get("error_message"),
                          byte_position=result.get("error_byte_position"))
        
        return {
            "test_passed": result["success"],
            "test_error_message": result.get("error_message"),
            "test_error_bytes": result.get("actual_bytes"),
            "test_byte_position": result.get("error_byte_position"),
            "tested_registers": result.get("tested_registers", []),
            "invalid_address": result.get("invalid_address"),
            "suggested_addresses": result.get("suggested_addresses", []),
            "attempt_logs": [log_entry]
        }
        
    except Exception as e:
        duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
        
        log_entry = {
            "attempt_number": state["current_attempt"],
            "agent_name": "Tester",
            "action": "test_driver",
            "success": False,
            "error_message": str(e),
            "duration_ms": duration_ms,
            "timestamp": datetime.utcnow().isoformat()
        }
        
        logger.exception("Tester agent failed unexpectedly")
        
        return {
            "test_passed": False,
            "test_error_message": str(e),
            "test_error_bytes": None,
            "test_byte_position": None,
            "tested_registers": [],
            "invalid_address": None,
            "suggested_addresses": [],
            "attempt_logs": [log_entry]
        }


def should_retry(state: AgentState) -> str:
    """
    Conditional edge: Decide whether to retry code generation.
    Returns the name of the next node.
    """
    if state["test_passed"]:
        logger.info("Test passed, proceeding to finalize")
        return "finalize"
    
    if state["current_attempt"] >= state["max_attempts"]:
        logger.warning("Max attempts reached, finalizing with failure",
                      attempts=state["current_attempt"])
        return "finalize"
    
    logger.info("Test failed, retrying code generation",
               attempt=state["current_attempt"],
               error=state.get("test_error_message"))
    
    return "increment_and_retry"


async def increment_attempt(state: AgentState) -> Dict[str, Any]:
    """
    Increment attempt counter before retry.
    CRITICAL: Also replace extracted_registers with suggested addresses!
    """
    suggested = state.get("suggested_addresses", [])
    
    if suggested:
        new_registers = [
            {
                "address": addr,
                "address_hex": f"0x{addr:04X}",
                "name": f"Register_{addr}",
                "data_type": "uint16",
                "function_code": 3
            }
            for addr in suggested[:10]
        ]
        
        logger.warning(
            "Replacing extracted_registers with suggested addresses for retry",
            old_count=len(state.get("extracted_registers", [])),
            new_count=len(new_registers),
            new_addresses=[r["address"] for r in new_registers]
        )
        
        return {
            "current_attempt": state["current_attempt"] + 1,
            "extracted_registers": new_registers
        }
    
    return {
        "current_attempt": state["current_attempt"] + 1
    }


async def finalize_result(state: AgentState) -> Dict[str, Any]:
    """
    Finalize the workflow result.
    Store experience in ChromaDB for future learning.
    """
    from experience_store.chroma_store import get_experience_store
    
    store = get_experience_store()
    
    if state["test_passed"]:
        base_confidence = 0.9
        attempt_penalty = (state["current_attempt"] - 1) * 0.1
        confidence = max(0.5, base_confidence - attempt_penalty)
    else:
        confidence = 0.1
    
    experience_id = None
    if state.get("test_error_message") or state["test_passed"]:
        try:
            experience_id = store.store_experience(
                protocol_text=state["raw_protocol_text"],
                problem_type="byte_mismatch" if state.get("test_error_bytes") else "general",
                error_message=state.get("test_error_message", "Success"),
                solution_applied=f"Generated code version {state['current_attempt']}",
                problematic_bytes=state.get("test_error_bytes"),
                byte_position=state.get("test_byte_position"),
                successful_code_snippet=state.get("generated_code")[:500] if state["test_passed"] else None,
                device_type=state.get("device_name"),
                success=state["test_passed"]
            )
        except Exception as e:
            logger.exception("Failed to store experience")
    
    logger.info("Workflow finalized",
               success=state["test_passed"],
               attempts=state["current_attempt"],
               confidence=confidence,
               experience_id=experience_id)
    
    return {
        "final_code": state.get("generated_code") if state["test_passed"] else None,
        "confidence_score": confidence,
        "experience_id": experience_id
    }
