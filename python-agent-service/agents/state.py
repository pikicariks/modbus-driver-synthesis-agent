"""State definition for LangGraph agent workflow."""
from typing import TypedDict, Optional, List, Annotated
from operator import add


class AgentState(TypedDict):
    """
    State that flows through the LangGraph workflow.
    Each agent reads from and writes to this state.
    """
    
    raw_protocol_text: str
    device_name: Optional[str]
    target_language: str
    previous_experience_context: Optional[str]
    
    parsed_specification: Optional[str]
    extracted_registers: List[dict]
    protocol_signature: Optional[str]
    
    generated_code: Optional[str]
    code_version: int
    
    test_passed: bool
    test_error_message: Optional[str]
    test_error_bytes: Optional[str]
    test_byte_position: Optional[int]
    tested_registers: List[str]
    invalid_address: Optional[int]  # Address that caused IllegalDataAddress
    suggested_addresses: List[int]  # Valid addresses to use instead
    
    current_attempt: int
    max_attempts: int
    
    attempt_logs: Annotated[List[dict], add]
    
    final_code: Optional[str]
    confidence_score: float
    experience_id: Optional[str]
