"""
FastAPI Application for Multi-Agent Driver Synthesis Service.

This service is called by .NET to synthesize Modbus drivers
using a LangGraph-based multi-agent system.
"""
import asyncio
from contextlib import asynccontextmanager
import structlog
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from config import get_settings
from models.schemas import (
    SynthesizeDriverRequest,
    SynthesizeDriverResponse,
    InternalAttemptLog,
    ModbusTestResult,
    TargetLanguage
)
from agents.workflow import synthesize_driver
from simulator.modbus_simulator import get_simulator

structlog.configure(
    processors=[
        structlog.stdlib.filter_by_level,
        structlog.stdlib.add_logger_name,
        structlog.stdlib.add_log_level,
        structlog.stdlib.PositionalArgumentsFormatter(),
        structlog.processors.TimeStamper(fmt="iso"),
        structlog.processors.StackInfoRenderer(),
        structlog.processors.format_exc_info,
        structlog.processors.UnicodeDecoder(),
        structlog.dev.ConsoleRenderer()
    ],
    wrapper_class=structlog.stdlib.BoundLogger,
    context_class=dict,
    logger_factory=structlog.stdlib.LoggerFactory(),
    cache_logger_on_first_use=True,
)

logger = structlog.get_logger()


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application lifespan manager."""
    settings = get_settings()
    
    logger.info("Starting Multi-Agent Driver Synthesis Service")
    
    simulator = get_simulator()
    simulator_task = asyncio.create_task(
        simulator.start(settings.modbus_host, settings.modbus_port)
    )
    
    logger.info("Modbus simulator started",
               host=settings.modbus_host,
               port=settings.modbus_port)
    
    yield
    
    logger.info("Shutting down service")
    simulator.stop()
    simulator_task.cancel()
    try:
        await simulator_task
    except asyncio.CancelledError:
        pass


app = FastAPI(
    title="Solar Driver Agent Service",
    description="""
    Multi-Agent System for autonomous Modbus driver synthesis.
    
    Uses LangGraph to orchestrate:
    - **Parser Agent**: Extracts protocol specification from PDF text
    - **Coder Agent**: Generates driver code using LLM
    - **Tester Agent**: Validates driver against Pymodbus simulator
    
    Implements a Generate → Test → Fix loop with experience-based learning via ChromaDB.
    """,
    version="1.0.0",
    lifespan=lifespan
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.post("/api/v1/synthesize-driver", response_model=SynthesizeDriverResponse)
async def synthesize_driver_endpoint(request: SynthesizeDriverRequest) -> SynthesizeDriverResponse:
    """
    Synthesize a Modbus driver from protocol text.
    
    This endpoint triggers the LangGraph multi-agent workflow:
    1. Parser Agent cleans and structures the protocol text
    2. Coder Agent generates driver code
    3. Tester Agent validates against Pymodbus simulator
    4. If test fails, retries with error context
    5. Returns validated code or failure details
    """
    logger.info("Received synthesize-driver request",
               device_name=request.device_name,
               target_language=request.target_language.value,
               text_length=len(request.protocol_text))
    
    try:
        result = await synthesize_driver(
            protocol_text=request.protocol_text,
            device_name=request.device_name,
            target_language=request.target_language.value,
            previous_experience=request.previous_experience
        )
        
        attempt_logs = []
        for log in result.get("attempt_logs", []):
            attempt_logs.append(InternalAttemptLog(
                attempt_number=log.get("attempt_number", 0),
                agent_name=log.get("agent_name", "Unknown"),
                action=log.get("action", ""),
                success=log.get("success", False),
                error_message=log.get("error_message"),
                duration_ms=log.get("duration_ms", 0)
            ))
        
        test_result = None
        if result.get("tested_registers"):
            test_result = ModbusTestResult(
                success=result.get("test_passed", False),
                tested_registers=result.get("tested_registers", []),
                expected_bytes=None,  # Could be added
                actual_bytes=result.get("test_error_bytes"),
                error_message=result.get("test_error_message"),
                error_byte_position=result.get("test_byte_position")
            )
        
        response = SynthesizeDriverResponse(
            success=result.get("test_passed", False),
            driver_code=result.get("final_code"),
            target_language=request.target_language,
            confidence_score=result.get("confidence_score", 0.0),
            internal_attempts=attempt_logs,
            total_internal_attempts=result.get("current_attempt", 0),
            test_result=test_result,
            extracted_registers=result.get("extracted_registers", []),
            error_message=result.get("test_error_message") if not result.get("test_passed") else None,
            experience_id=result.get("experience_id")
        )
        
        logger.info("Returning synthesize-driver response",
                   success=response.success,
                   confidence=response.confidence_score,
                   attempts=response.total_internal_attempts)
        
        return response
        
    except Exception as e:
        logger.exception("Error in synthesize-driver endpoint")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return {"status": "healthy", "service": "solar-driver-agent"}


@app.get("/api/v1/experiences")
async def get_experiences(limit: int = 50, device: str = None):
    """Get experiences from ChromaDB."""
    from experience_store.chroma_store import get_experience_store
    
    try:
        store = get_experience_store()
        
        results = store._collection.get(include=["metadatas", "documents"])
        
        experiences = []
        for i in range(len(results.get("ids", []))):
            meta = results["metadatas"][i] if results.get("metadatas") else {}
            
            if device and meta.get("device_type", "").lower() != device.lower():
                continue
            
            experiences.append({
                "id": results["ids"][i],
                "device_type": meta.get("device_type", "Unknown"),
                "problem_type": meta.get("problem_type", "Unknown"),
                "success": meta.get("success", False),
                "error_message": meta.get("error_message", ""),
                "solution_applied": meta.get("solution_applied", ""),
                "problematic_bytes": meta.get("problematic_bytes", ""),
                "byte_position": meta.get("byte_position", -1),
                "protocol_signature": meta.get("protocol_signature", ""),
                "created_at": meta.get("created_at", "")
            })
        
        experiences.sort(key=lambda x: x.get("created_at", ""), reverse=True)
        experiences = experiences[:limit]
        
        total = len(experiences)
        successful = sum(1 for e in experiences if e.get("success"))
        
        return {
            "total": store._collection.count(),
            "returned": len(experiences),
            "summary": {
                "successful": successful,
                "failed": total - successful
            },
            "experiences": experiences
        }
    except Exception as e:
        logger.exception("Error getting experiences")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/api/v1/experiences/{experience_id}")
async def get_experience_by_id(experience_id: str):
    """Get a specific experience by ID."""
    from experience_store.chroma_store import get_experience_store
    
    try:
        store = get_experience_store()
        results = store._collection.get(
            ids=[experience_id],
            include=["metadatas", "documents"]
        )
        
        if not results.get("ids"):
            raise HTTPException(status_code=404, detail="Experience not found")
        
        meta = results["metadatas"][0] if results.get("metadatas") else {}
        doc = results["documents"][0] if results.get("documents") else ""
        
        return {
            "id": experience_id,
            "metadata": meta,
            "document": doc
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.exception("Error getting experience")
        raise HTTPException(status_code=500, detail=str(e))


if __name__ == "__main__":
    import uvicorn
    settings = get_settings()
    uvicorn.run(
        "main:app",
        host="0.0.0.0",
        port=8000,
        reload=True,
        log_level=settings.log_level.lower()
    )
