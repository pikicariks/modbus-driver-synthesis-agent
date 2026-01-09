"""Configuration for the Agent Service."""
from pydantic_settings import BaseSettings
from functools import lru_cache


class Settings(BaseSettings):
    """Application settings loaded from environment variables."""
    
    # OpenAI
    openai_api_key: str = ""
    openai_model: str = "gpt-4o-mini"  # Jeftiniji i šire dostupan model
    
    # ChromaDB
    chroma_persist_directory: str = "./chroma_db"
    chroma_collection_name: str = "protocol_experiences"
    
    # Modbus Simulator
    modbus_host: str = "127.0.0.1"
    modbus_port: int = 5020
    
    # Agent Settings
    max_internal_retries: int = 3  # default internal LangGraph retries
    code_generation_temperature: float = 0.2
    
    # Logging
    log_level: str = "DEBUG"
    
    class Config:
        env_file = ".env"
        env_file_encoding = "utf-8"


@lru_cache
def get_settings() -> Settings:
    return Settings()
