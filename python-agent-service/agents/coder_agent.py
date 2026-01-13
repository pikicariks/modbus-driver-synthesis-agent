"""Coder Agent - Generates Modbus driver code."""
from typing import Dict, Any
import structlog
from datetime import datetime

from langchain_openai import ChatOpenAI
from langchain_core.prompts import ChatPromptTemplate

from config import get_settings
from agents.state import AgentState

logger = structlog.get_logger()


CODER_PROMPT_PYTHON = ChatPromptTemplate.from_messages([
    ("system", """You are an expert Python developer specializing in Modbus communication.
You generate production-ready driver code for solar inverters using pymodbus 3.x.

CRITICAL IMPORT REQUIREMENTS (pymodbus 3.x syntax):
- Use: `from pymodbus.client import AsyncModbusTcpClient`
- Do NOT use: `from pymodbus.client.async import ...` (async is a reserved keyword!)
- Do NOT use old pymodbus 2.x imports

Your code MUST:
1. Use `from pymodbus.client import AsyncModbusTcpClient` for async communication
2. Handle byte ordering correctly (big-endian for Modbus)
3. Include proper error handling with try/except
4. Include a `run_self_test()` async function that validates basic connectivity
5. Parse multi-register values (32-bit) correctly using struct.unpack
6. Apply scale factors where specified
7. Be fully executable without modifications
8. Use `await client.connect()` and `client.close()` for connection management

Example imports:
```python
import asyncio
import struct
from pymodbus.client import AsyncModbusTcpClient
from pymodbus.exceptions import ModbusIOException
```

The generated driver should be a class that can:
- Connect to the Modbus device
- Read all specified registers
- Return values in human-readable format

{experience_context}"""),
    
    ("human", """Generate a Python Modbus driver for the following specification:

Device: {device_name}

Protocol Specification:
{specification}

Extracted Registers:
{registers}

{error_context}

Generate complete, working Python code with a `run_self_test()` function.""")
])


CODER_PROMPT_CSHARP = ChatPromptTemplate.from_messages([
    ("system", """You are an expert C# developer specializing in Modbus communication.
You generate production-ready driver code for solar inverters.

Your code MUST:
1. Use NModbus4 or similar library patterns
2. Handle byte ordering correctly (big-endian for Modbus)
3. Include proper error handling with try-catch
4. Include a `RunSelfTest()` method that returns bool
5. Parse multi-register values (32-bit) correctly
6. Apply scale factors where specified
7. Be fully compilable

{experience_context}"""),
    
    ("human", """Generate a C# Modbus driver for the following specification:

Device: {device_name}

Protocol Specification:
{specification}

Extracted Registers:
{registers}

{error_context}

Generate complete, working C# code with a `RunSelfTest()` method.""")
])


def fix_common_code_issues(code: str) -> str:
    """
    Post-process generated code to fix common issues.
    """
    import re
    
    code = code.replace("from pymodbus.client.async import AsyncModbusTcpClient", 
                        "from pymodbus.client import AsyncModbusTcpClient")
    
    if "await client.connect()" not in code:
        code = code.replace("client = AsyncModbusTcpClient(", "client = AsyncModbusTcpClient(")
    
    pattern = r"for\s+([A-Za-z_][A-Za-z0-9_]*)\s*,\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\s*=False\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\s*=False\s*\)\s*in\s*([A-Za-z_][A-Za-z0-9_]*)\.items\(\):"
    match = re.search(pattern, code)
    if match:
        addr_var, name_var, scale_var, signed_var, bit_var, map_var = match.groups()
        replacement = (
            f"for {addr_var}, (tmp_name, tmp_scale, tmp_signed, tmp_is32) in {map_var}.items():\n"
            f"        {name_var} = tmp_name\n"
            f"        {scale_var} = tmp_scale\n"
            f"        {signed_var} = tmp_signed if tmp_signed is not None else False\n"
            f"        {bit_var} = tmp_is32 if tmp_is32 is not None else False"
        )
        code = re.sub(pattern, replacement, code)
    
    return code


async def generate_code(state: AgentState) -> Dict[str, Any]:
    """
    Coder Agent node.
    Generates driver code based on specification.
    Uses previous errors as context for fixes.
    """
    start_time = datetime.utcnow()
    
    logger.info("Coder agent generating code",
               attempt=state["current_attempt"],
               language=state["target_language"])
    
    settings = get_settings()
    
    base_temp = settings.code_generation_temperature
    if state["current_attempt"] > 1:
        temperature = min(0.7, base_temp + 0.3)
        logger.info("Using higher temperature for retry", 
                   attempt=state["current_attempt"], temperature=temperature)
    else:
        temperature = base_temp
    
    llm = ChatOpenAI(
        model=settings.openai_model,
        temperature=temperature,
        api_key=settings.openai_api_key
    )
    
    error_context = ""
    if state["current_attempt"] > 1 and state.get("test_error_message"):
        error_msg = state['test_error_message']
        
        invalid_addr = state.get("invalid_address")
        
        suggested = state.get("suggested_addresses", [])
        
        if "ILLEGAL" in error_msg.upper() or "does not exist" in error_msg:
            if suggested:
                valid_addrs_str = ", ".join([str(a) for a in suggested[:10]])
            else:
                valid_addrs = [r.get('address', 30000) for r in state.get("extracted_registers", [])]
                valid_addrs_str = ", ".join([str(a) for a in valid_addrs[:10]]) if valid_addrs else "30000, 30001, 30002, 30003, 30004"
            
            error_context = f"""
CRITICAL FIX REQUIRED - YOUR PREVIOUS CODE FAILED!

PROBLEM: Your code tried to read register address {invalid_addr or 'INVALID'} which DOES NOT EXIST.

The Modbus simulator rejected your request with: IllegalDataAddress (0x02).

SOLUTION - USE THESE ADDRESSES:
{valid_addrs_str}

YOUR PREVIOUS MISTAKE:
You used addresses like 0x0000, 0x0001, 0x0002, 0x0003, etc.
These are wrong. The solar inverter uses addresses starting at 30000.

EXAMPLE OF CORRECT CODE:
```python
# WRONG (previous):
response = await client.read_holding_registers(0x0001, count=1)

# CORRECT (use now):
response = await client.read_holding_registers(30000, count=1)
```

DO NOT REPEAT THE SAME MISTAKE. Use addresses: {valid_addrs_str}
"""
            logger.warning(
                "RETRY with corrected addresses",
                attempt=state["current_attempt"],
                invalid_addr=invalid_addr,
                suggested=suggested[:5]
            )
        else:
            error_context = f"""
PREVIOUS CODE FAILED - FIX REQUIRED:

Error: {error_msg}

Problematic bytes: {state.get('test_error_bytes', 'N/A')}
Byte position: {state.get('test_byte_position', 'N/A')}

Please fix this specific issue in the new code.
"""
    
    experience_context = ""
    if state.get("previous_experience_context"):
        experience_context = f"""
## Learning from Past Experiences:
{state['previous_experience_context']}

Use these past experiences to avoid similar mistakes.
"""
    
    registers_str = "\n".join([
        f"- {r.get('address_hex', '0x0000')}: {r.get('name', 'Unknown')} ({r.get('data_type', 'uint16')})"
        for r in state.get("extracted_registers", [])
    ]) or "No specific registers extracted - use specification text."
    
    try:
        if state["target_language"].lower() == "csharp":
            prompt = CODER_PROMPT_CSHARP
        else:
            prompt = CODER_PROMPT_PYTHON
        
        chain = prompt | llm
        response = await chain.ainvoke({
            "device_name": state.get("device_name", "Solar Inverter"),
            "specification": state.get("parsed_specification", state["raw_protocol_text"])[:10000],
            "registers": registers_str,
            "error_context": error_context,
            "experience_context": experience_context
        })
        
        raw_response = response.content
        
        logger.debug("Raw LLM response (first 500 chars)", 
                    raw_preview=raw_response[:500] if raw_response else "EMPTY")
        
        generated_code = extract_code_block(raw_response, state["target_language"])
        
        generated_code = fix_common_code_issues(generated_code)
        
        logger.debug("Extracted code (first 300 chars)",
                    code_preview=generated_code[:300] if generated_code else "EMPTY")
        
        duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
        
        log_entry = {
            "attempt_number": state["current_attempt"],
            "agent_name": "Coder",
            "action": "generate_code",
            "success": True,
            "error_message": None,
            "duration_ms": duration_ms,
            "timestamp": datetime.utcnow().isoformat()
        }
        
        logger.info("Coder agent completed",
                   code_length=len(generated_code))
        
        return {
            "generated_code": generated_code,
            "code_version": state["current_attempt"],
            "attempt_logs": [log_entry]
        }
        
    except Exception as e:
        duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
        
        log_entry = {
            "attempt_number": state["current_attempt"],
            "agent_name": "Coder",
            "action": "generate_code",
            "success": False,
            "error_message": str(e),
            "duration_ms": duration_ms,
            "timestamp": datetime.utcnow().isoformat()
        }
        
        logger.exception("Coder agent failed")
        
        return {
            "generated_code": None,
            "code_version": state["current_attempt"],
            "attempt_logs": [log_entry]
        }


def fix_common_code_issues(code: str) -> str:
    """Fix common LLM-generated code issues."""
    import re
    
    code = re.sub(
        r'from\s+pymodbus\.client\.async\s+import',
        'from pymodbus.client import',
        code
    )
    
    code = re.sub(
        r'from\s+pymodbus\.client\.asynchronous\.tcp\s+import\s+AsyncModbusTCPClient',
        'from pymodbus.client import AsyncModbusTcpClient',
        code
    )
    
    code = re.sub(
        r'AsyncModbusTCPClient',
        'AsyncModbusTcpClient',
        code
    )
    
    return code


def extract_code_block(text: str, language: str) -> str:
    """Extract code block from markdown-formatted response."""
    import re
    
    text = text.strip()
    
    lang_marker = "python" if language.lower() == "python" else "csharp|cs|c#"
    pattern1 = rf"```(?:{lang_marker})\s*\n(.*?)```"
    matches = re.findall(pattern1, text, re.DOTALL | re.IGNORECASE)
    if matches:
        return matches[0].strip()
    
    pattern2 = r"```\s*\n(.*?)```"
    matches = re.findall(pattern2, text, re.DOTALL)
    if matches:
        return matches[0].strip()
    
    pattern3 = r"```(?:python|py)?(.*?)```"
    matches = re.findall(pattern3, text, re.DOTALL | re.IGNORECASE)
    if matches:
        return matches[0].strip()
    
    if text.startswith("```"):
        lines = text.split("\n")
        if lines[0].strip().startswith("```"):
            lines = lines[1:]
        if lines and lines[-1].strip() == "```":
            lines = lines[:-1]
        return "\n".join(lines).strip()
    
    return text
