"""State definition for LangGraph agent workflow."""
from typing import TypedDict, Optional, List, Annotated
from operator import add


class AgentState(TypedDict):
    """
    State that flows through the LangGraph workflow.
    Each agent reads from and writes to this state.
    """
    
    # Input
    raw_protocol_text: str
    device_name: Optional[str]
    target_language: str
    previous_experience_context: Optional[str]
    
    # Parser output
    parsed_specification: Optional[str]
    extracted_registers: List[dict]
    protocol_signature: Optional[str]
    
    # Coder output
    generated_code: Optional[str]
    code_version: int
    
    # Tester output
    test_passed: bool
    test_error_message: Optional[str]
    test_error_bytes: Optional[str]
    test_byte_position: Optional[int]
    tested_registers: List[str]
    invalid_address: Optional[int]  # Address that caused IllegalDataAddress
    suggested_addresses: List[int]  # Valid addresses to use instead
    
    # Flow control
    current_attempt: int
    max_attempts: int
    
    # Accumulated logs (using reducer to append)
    attempt_logs: Annotated[List[dict], add]
    
    # Final output
    final_code: Optional[str]
    confidence_score: float
    experience_id: Optional[str]
