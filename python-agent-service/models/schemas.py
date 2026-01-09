"""Pydantic schemas for API requests and responses."""
from pydantic import BaseModel, Field
from typing import Optional, List
from enum import Enum
from datetime import datetime


class TargetLanguage(str, Enum):
    """Supported target languages for driver generation."""
    PYTHON = "python"
    CSHARP = "csharp"


class SynthesizeDriverRequest(BaseModel):
    """Request to synthesize a Modbus driver from protocol text."""
    
    protocol_text: str = Field(
        ...,
        description="Extracted text from the PDF protocol documentation"
    )
    previous_experience: Optional[str] = Field(
        default=None,
        description="Previous error context for retry attempts"
    )
    target_language: TargetLanguage = Field(
        default=TargetLanguage.PYTHON,
        description="Target programming language for the driver"
    )
    device_name: Optional[str] = Field(
        default=None,
        description="Name of the device/inverter"
    )


class InternalAttemptLog(BaseModel):
    """Log of a single internal agent attempt."""
    
    attempt_number: int
    agent_name: str
    action: str
    success: bool
    error_message: Optional[str] = None
    duration_ms: int
    timestamp: datetime = Field(default_factory=datetime.utcnow)


class ModbusTestResult(BaseModel):
    """Result of testing driver against Pymodbus simulator."""
    
    success: bool
    tested_registers: List[str] = Field(default_factory=list)
    expected_bytes: Optional[str] = None
    actual_bytes: Optional[str] = None
    error_message: Optional[str] = None
    error_byte_position: Optional[int] = None


class SynthesizeDriverResponse(BaseModel):
    """Response containing synthesized driver and metadata."""
    
    success: bool
    driver_code: Optional[str] = None
    target_language: TargetLanguage
    
    # Reliability metrics
    confidence_score: float = Field(
        ge=0.0, le=1.0,
        description="Confidence score from 0 to 1"
    )
    
    # Internal attempt logs
    internal_attempts: List[InternalAttemptLog] = Field(default_factory=list)
    total_internal_attempts: int = 0
    
    # Test results
    test_result: Optional[ModbusTestResult] = None
    
    # Extracted protocol info
    extracted_registers: List[dict] = Field(default_factory=list)
    
    # Error info if failed
    error_message: Optional[str] = None
    
    # Experience ID for tracking
    experience_id: Optional[str] = None


class ExperienceRecord(BaseModel):
    """Record stored in ChromaDB for RAG."""
    
    id: str
    protocol_signature: str  # Hash ili ključne karakteristike protokola
    device_type: Optional[str] = None
    
    # Problem details
    problem_type: str  # "byte_mismatch", "register_error", "compilation_error"
    problematic_bytes: Optional[str] = None
    byte_position: Optional[int] = None
    error_message: str
    
    # Solution
    solution_applied: str
    successful_code_snippet: Optional[str] = None
    
    # Metadata
    created_at: datetime = Field(default_factory=datetime.utcnow)
    success: bool = False
