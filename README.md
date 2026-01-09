#  Modbus Driver Synthesis Agent

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Python](https://img.shields.io/badge/Python-3.11+-3776AB?logo=python&logoColor=white)](https://www.python.org/)
[![LangGraph](https://img.shields.io/badge/LangGraph-Multi--Agent-FF6B35)](https://langchain-ai.github.io/langgraph/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> **Autonomous Multi-Agent System for generating Modbus drivers from PDF documentation**

An AI-powered system that automatically reads solar inverter PDF specifications and generates working, validated Modbus communication drivers. Features a self-healing **Generate → Test → Fix** loop with experience-based learning.

![Architecture Overview](https://img.shields.io/badge/Architecture-Sense--Think--Act--Learn-blue)

---

##  Features

| Feature | Description |
|---------|-------------|
|  **Multi-Agent System** | Parser, Coder, and Tester agents orchestrated via LangGraph |
|  **PDF Processing** | Extracts Modbus register specifications from manufacturer PDFs |
|  **Code Generation** | GPT-4 powered Python driver code generation |
|  **Binary Validation** | Tests generated code against real Pymodbus simulator |
|  **Self-Healing** | Automatic retry with error context when tests fail |
|  **RAG Learning** | Stores experiences in ChromaDB for future improvements |
|  **Real-Time UI** | Blazor + SignalR for live processing updates |
|  **Resilience** | Polly-based retry and circuit breaker patterns |

---
──────────────────────────────────────────────────────────┘
```

---

##  Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Python 3.11+](https://www.python.org/downloads/)
- [OpenAI API Key](https://platform.openai.com/api-keys)

### 1. Clone the repository

```bash
git clone https://github.com/yourusername/modbus-driver-synthesis-agent.git
cd modbus-driver-synthesis-agent
```

### 2. Setup Python Agent Service

```bash
cd python-agent-service

# Create virtual environment
python -m venv venv

# Activate (Windows)
venv\Scripts\activate
# Activate (Linux/Mac)
# source venv/bin/activate

# Install dependencies
pip install -r requirements.txt

# Create .env file
echo "OPENAI_API_KEY=sk-your-key-here" > .env
```

### 3. Start Python Service

```bash
cd python-agent-service
uvicorn main:app --reload --port 8000
```

### 4. Start .NET Application

```bash
# In a new terminal
cd AiAgents.SolarDriverAgent.Web
dotnet run
```

### 5. Open Browser

Navigate to `https://localhost:5001`

---

##  Project Structure

```
modbus-driver-synthesis-agent/
│
├── AiAgents.Core/                    # Core agent abstractions
│   ├── SoftwareAgent.cs              # Generic Sense-Think-Act-Learn base
│   └── Abstractions/                 # Interfaces (IPolicy, IActuator, etc.)
│
├── AiAgents.SolarDriverAgent/        # Application layer
│   ├── Application/
│   │   └── Runner/
│   │       └── DriverSynthesisRunner.cs  # Main agent implementation
│   ├── Domain/                       # Entities, Enums, Repositories
│   └── Infrastructure/               # EF Core, HTTP clients, PDF extraction
│
├── AiAgents.SolarDriverAgent.Web/    # Web host
│   ├── BackgroundServices/           # AgentHostedService
│   ├── Components/Pages/             # Blazor pages
│   ├── Hubs/                         # SignalR hub
│   └── Controllers/                  # REST API
│
├── python-agent-service/             # Python multi-agent system
   ├── agents/
   │   ├── workflow.py               # LangGraph workflow definition
   │   ├── parser_agent.py           # Extracts specs from PDF
   │   ├── coder_agent.py            # Generates driver code
   │   └── tester_agent.py           # Validates against simulator
   ├── simulator/
   │   └── modbus_simulator.py       # Pymodbus TCP server
   ├── experience_store/
   │   └── chroma_store.py           # ChromaDB RAG implementation
    └── main.py                       # FastAPI entry point

```

---

##  Configuration

### .NET (`appsettings.json`)

```json
{
  "Agent": {
    "TickIntervalSeconds": 5,
    "IdleIntervalSeconds": 30
  },
  "LlmClient": {
    "BaseUrl": "http://localhost:8000",
    "TimeoutSeconds": 120
  }
}
```

### Python (`.env`)

```env
OPENAI_API_KEY=sk-...
OPENAI_MODEL=gpt-4o-mini
MODBUS_HOST=127.0.0.1
MODBUS_PORT=5020
MAX_INTERNAL_RETRIES=3
```

---

##  How It Works

### The Generate → Test → Fix Loop

```
1.  Upload PDF          User uploads solar inverter documentation
                               │
2.  Parser Agent        Extracts Modbus register addresses, data types
                               │
3.  Coder Agent         Generates Python driver code using GPT-4
                               │
4.  Tester Agent        Executes code against Modbus simulator
                               │
         ┌─────────────────────┴─────────────────────┐
         │                                           │
     Test Fails                               Test Passes
         │                                           │
    Error context added                        Store experience
    to next attempt                            Return validated code
         │                                           │
     Retry (max 3)                            Save to ChromaDB
         │
    Back to Coder Agent
```

### Self-Healing Example

**Attempt 1 (Fails):**
```python
# Coder generates code with wrong address
response = await client.read_holding_registers(0x0001, count=1)
#  Simulator returns: IllegalDataAddress (0x02)
```

**Attempt 2 (Succeeds):**
```python
# Coder receives error context and generates corrected code
response = await client.read_holding_registers(30000, count=1)
#  Simulator returns valid data
```

---

##  Technologies

### Backend (.NET 8)
- ASP.NET Core (Web API + Blazor Server)
- Entity Framework Core + SQLite
- SignalR (Real-time communication)
- Polly (Resilience patterns)
- iTextSharp (PDF extraction)

### Python Agent Service
- FastAPI (Async REST API)
- LangGraph (Agent orchestration)
- LangChain + OpenAI (LLM integration)
- ChromaDB (Vector database for RAG)
- Pymodbus 3.x (Modbus TCP server/client)
- Pydantic (Schema validation)
- Structlog (Structured logging)

---

##  Academic Context

This project demonstrates:

| Concept | Implementation |
|---------|---------------|
| **Autonomous Agents** | Sense-Think-Act-Learn cycle |
| **Multi-Agent Systems** | LangGraph orchestration |
| **Self-Healing Systems** | Automatic error correction |
| **RAG (Retrieval-Augmented Generation)** | ChromaDB experience store |
| **LLM Code Generation** | GPT-4 with structured prompts |
| **Binary-Level Validation** | Pymodbus simulator testing |
| **Real-Time Systems** | SignalR websocket communication |
| **Clean Architecture** | Layered .NET application |

---

##  License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

##  Acknowledgments

- [LangChain](https://langchain.com/) / [LangGraph](https://langchain-ai.github.io/langgraph/) for agent orchestration
- [OpenAI](https://openai.com/) for GPT-4 API
- [ChromaDB](https://www.trychroma.com/) for vector storage
- [Pymodbus](https://pymodbus.readthedocs.io/) for Modbus simulation

---

