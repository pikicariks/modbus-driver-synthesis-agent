"""
LangGraph Workflow for Multi-Agent Driver Synthesis.

This workflow orchestrates the Parser, Coder, and Tester agents
in a Generate → Test → Fix loop.
"""
from typing import Optional
import structlog

from langgraph.graph import StateGraph, END

from config import get_settings
from agents.state import AgentState
from agents.parser_agent import parse_protocol
from agents.coder_agent import generate_code
from agents.tester_agent import test_driver, should_retry, increment_attempt, finalize_result
from experience_store.chroma_store import get_experience_store

logger = structlog.get_logger()


def create_driver_synthesis_workflow() -> StateGraph:
    """
    Create the LangGraph workflow for driver synthesis.
    
    Workflow:
    1. Parser Agent - Extracts specification from protocol text
    2. Coder Agent - Generates driver code
    3. Tester Agent - Validates against Pymodbus simulator
    4. Conditional: If test fails and attempts < max, go back to Coder
    5. Finalize - Store experience and return result
    """
    
    # Create the graph
    workflow = StateGraph(AgentState)
    
    # Add nodes
    workflow.add_node("parser", parse_protocol)
    workflow.add_node("coder", generate_code)
    workflow.add_node("tester", test_driver)
    workflow.add_node("increment_and_retry", increment_attempt)
    workflow.add_node("finalize", finalize_result)
    
    # Add edges
    workflow.set_entry_point("parser")
    workflow.add_edge("parser", "coder")
    workflow.add_edge("coder", "tester")
    
    # Conditional edge: retry or finalize
    workflow.add_conditional_edges(
        "tester",
        should_retry,
        {
            "increment_and_retry": "increment_and_retry",
            "finalize": "finalize"
        }
    )
    
    # After incrementing, go back to coder
    workflow.add_edge("increment_and_retry", "coder")
    
    # Finalize ends the workflow
    workflow.add_edge("finalize", END)
    
    return workflow.compile()


# Compiled workflow singleton
_workflow = None


def get_workflow():
    """Get or create the compiled workflow."""
    global _workflow
    if _workflow is None:
        _workflow = create_driver_synthesis_workflow()
    return _workflow


async def synthesize_driver(
    protocol_text: str,
    device_name: Optional[str] = None,
    target_language: str = "python",
    previous_experience: Optional[str] = None
) -> AgentState:
    """
    Run the complete driver synthesis workflow.
    
    This is the main entry point called by the FastAPI endpoint.
    """
    settings = get_settings()
    
    logger.info("Starting driver synthesis workflow",
               device_name=device_name,
               target_language=target_language,
               text_length=len(protocol_text))
    
    # SENSE phase: Query ChromaDB for relevant past experiences
    experience_context = previous_experience
    if not experience_context:
        try:
            store = get_experience_store()
            similar_experiences = store.find_similar_experiences(
                protocol_text,
                limit=3
            )
            if similar_experiences:
                experience_context = store.format_experiences_for_context(similar_experiences)
                logger.info("Found similar experiences from ChromaDB",
                           count=len(similar_experiences))
        except Exception as e:
            logger.warning("Failed to query experience store", error=str(e))
    
    # Initialize state
    initial_state: AgentState = {
        "raw_protocol_text": protocol_text,
        "device_name": device_name,
        "target_language": target_language,
        "previous_experience_context": experience_context,
        
        "parsed_specification": None,
        "extracted_registers": [],
        "protocol_signature": None,
        
        "generated_code": None,
        "code_version": 0,
        
        "test_passed": False,
        "test_error_message": None,
        "test_error_bytes": None,
        "test_byte_position": None,
        "tested_registers": [],
        "invalid_address": None,
        "suggested_addresses": [],
        
        "current_attempt": 1,
        "max_attempts": settings.max_internal_retries,
        
        "attempt_logs": [],
        
        "final_code": None,
        "confidence_score": 0.0,
        "experience_id": None
    }
    
    # Run the workflow
    workflow = get_workflow()
    final_state = await workflow.ainvoke(initial_state)
    
    logger.info("Driver synthesis workflow completed",
               success=final_state.get("test_passed", False),
               attempts=final_state.get("current_attempt", 0),
               confidence=final_state.get("confidence_score", 0))
    
    return final_state
