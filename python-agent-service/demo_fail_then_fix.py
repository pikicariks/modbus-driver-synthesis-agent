"""
Demo script: Shows the "Fail → Learn → Fix → Success" cycle.

This script simulates uploading a PDF with WRONG addresses (0x0001, 0x0002, etc.)
The agent will:
1. First attempt: Use wrong addresses → FAIL (IllegalDataAddress)
2. Second attempt: Learn from error, use correct addresses (30000+) → SUCCESS
"""
import asyncio
import httpx
import json
from datetime import datetime

# PDF text with WRONG addresses (0x0001, 0x0002, etc.)
# The simulator only accepts 30000+ addresses!
DEMO_PDF_TEXT = """
SOLAR INVERTER MODBUS PROTOCOL v1.2
===================================
Model: DemoSolar INV-3000
Protocol: Modbus TCP

REGISTER MAP
------------

Register Address | Name              | Type    | Unit  | Description
-----------------|-------------------|---------|-------|-------------
0x0001           | Power_Output      | uint16  | W     | Current power output
0x0002           | AC_Voltage        | uint16  | V     | AC voltage (x0.1)
0x0003           | AC_Current        | uint16  | A     | AC current (x0.01)
0x0004           | Temperature       | int16   | °C    | Internal temperature
0x0005           | Status_Code       | uint16  | -     | Operating status
0x0006           | Error_Code        | uint16  | -     | Active error code

COMMUNICATION PARAMETERS
------------------------
- Slave ID: 1
- Function Code: 3 (Read Holding Registers)
- Byte Order: Big-endian

STATUS CODES
------------
0 = Standby
1 = Running
2 = Fault
3 = Maintenance

NOTE: All registers are 16-bit values. Use function code 3.
"""


async def run_demo():
    """Run the fail-then-fix demo."""
    print("=" * 70)
    print("🎬 DEMO: Fail → Learn → Fix → Success Cycle")
    print("=" * 70)
    print()
    print("📄 Input PDF has WRONG addresses: 0x0001, 0x0002, 0x0003...")
    print("🔧 Simulator only accepts: 30000, 30001, 30002...")
    print()
    print("Expected flow:")
    print("  1️⃣  First attempt: LLM uses 0x0001 → FAIL (IllegalDataAddress)")
    print("  2️⃣  Agent learns: Gets suggested addresses 30000+")
    print("  3️⃣  Second attempt: LLM uses 30000 → SUCCESS ✅")
    print()
    print("-" * 70)
    print()
    
    async with httpx.AsyncClient(timeout=120.0) as client:
        request_data = {
            "protocol_text": DEMO_PDF_TEXT,
            "device_name": "DemoSolar_FailThenFix",
            "target_language": "python"
        }
        
        print(f"🚀 Sending request to Python agent service...")
        print(f"   Time: {datetime.now().strftime('%H:%M:%S')}")
        print()
        
        try:
            response = await client.post(
                "http://localhost:8000/api/v1/synthesize-driver",
                json=request_data
            )
            
            result = response.json()
            
            print("=" * 70)
            print("📊 RESULTS")
            print("=" * 70)
            print()
            
            # Show success/fail
            if result.get("success"):
                print("✅ FINAL STATUS: SUCCESS!")
            else:
                print("❌ FINAL STATUS: FAILED")
            print()
            
            # Show attempt logs
            print("📋 ATTEMPT LOGS:")
            print("-" * 50)
            
            attempt_logs = result.get("attempt_logs", [])
            for log in attempt_logs:
                agent = log.get("agent_name", "Unknown")
                success = "✅" if log.get("success") else "❌"
                error = log.get("error_message", "")
                duration = log.get("duration_ms", 0)
                attempt = log.get("attempt_number", 1)
                
                print(f"  [{attempt}] {agent}: {success} ({duration}ms)")
                if error:
                    print(f"      Error: {error[:100]}...")
            
            print()
            
            # Show total attempts
            total_attempts = result.get("total_internal_attempts", 1)
            confidence = result.get("confidence_score", 0)
            
            print(f"📈 SUMMARY:")
            print(f"   Total Internal Attempts: {total_attempts}")
            print(f"   Confidence Score: {confidence:.0%}")
            
            # Check if it demonstrates fail-then-fix
            if total_attempts > 1 and result.get("success"):
                print()
                print("🎉 " + "=" * 66)
                print("🎉 DEMO SUCCESS: Agent failed first, learned, and fixed the code!")
                print("🎉 " + "=" * 66)
            elif total_attempts == 1 and result.get("success"):
                print()
                print("⚠️  Note: Agent succeeded on first try (LLM may have guessed correctly)")
            else:
                print()
                print("⚠️  Note: Agent did not succeed after retries")
            
            # Show generated code snippet
            if result.get("generated_code"):
                code = result["generated_code"]
                print()
                print("📝 GENERATED CODE (first 30 lines):")
                print("-" * 50)
                lines = code.split('\n')[:30]
                for i, line in enumerate(lines, 1):
                    print(f"  {i:3}: {line}")
                if len(code.split('\n')) > 30:
                    print("  ... (truncated)")
            
        except httpx.HTTPError as e:
            print(f"❌ HTTP Error: {e}")
        except Exception as e:
            print(f"❌ Error: {e}")
            import traceback
            traceback.print_exc()


if __name__ == "__main__":
    print()
    print("Make sure Python agent service is running on http://localhost:8000")
    print("(Run: uvicorn main:app --reload --port 8000)")
    print()
    
    asyncio.run(run_demo())
