"""ChromaDB Experience Store for RAG-based learning."""
import chromadb
from chromadb.config import Settings as ChromaSettings
from typing import List, Optional
import hashlib
import json
from datetime import datetime
import structlog

from config import get_settings
from models.schemas import ExperienceRecord

logger = structlog.get_logger()


class ExperienceStore:
    """
    ChromaDB-based experience store for storing and retrieving
    protocol-specific experiences and solutions.
    
    Enables the agent to "remember" specific byte-level problems
    and their solutions for similar protocols.
    """
    
    def __init__(self):
        settings = get_settings()
        
        self._client = chromadb.PersistentClient(
            path=settings.chroma_persist_directory,
            settings=ChromaSettings(anonymized_telemetry=False)
        )
        
        self._collection = self._client.get_or_create_collection(
            name=settings.chroma_collection_name,
            metadata={"description": "Protocol experiences and solutions"}
        )
        
        logger.info(
            "Experience store initialized",
            collection=settings.chroma_collection_name,
            count=self._collection.count()
        )
    
    def compute_protocol_signature(self, protocol_text: str) -> str:
        """
        Compute a signature for a protocol based on key characteristics.
        Used for finding similar protocols.
        """
        features = []
        
        import re
        register_pattern = r'(?:register|address|addr)[:\s]*(?:0x)?([0-9a-fA-F]+)'
        registers = re.findall(register_pattern, protocol_text.lower())
        features.extend(sorted(set(registers))[:10])  # Top 10 unique registers
        
        func_pattern = r'(?:function|func)[:\s]*(?:code)?[:\s]*(\d+)'
        func_codes = re.findall(func_pattern, protocol_text.lower())
        features.extend(sorted(set(func_codes)))
        
        type_keywords = ['int16', 'int32', 'uint16', 'uint32', 'float', 'string']
        for kw in type_keywords:
            if kw in protocol_text.lower():
                features.append(kw)
        
        signature = "_".join(features) if features else "generic"
        return hashlib.md5(signature.encode()).hexdigest()[:16]
    
    def store_experience(
        self,
        protocol_text: str,
        problem_type: str,
        error_message: str,
        solution_applied: str,
        problematic_bytes: Optional[str] = None,
        byte_position: Optional[int] = None,
        successful_code_snippet: Optional[str] = None,
        device_type: Optional[str] = None,
        success: bool = False
    ) -> str:
        """
        Store an experience in ChromaDB.
        Returns the experience ID.
        """
        error_msg = error_message or "Success"
        solution = solution_applied or "N/A"
        
        experience_id = f"exp_{datetime.utcnow().strftime('%Y%m%d%H%M%S')}_{hashlib.md5(error_msg.encode()).hexdigest()[:8]}"
        protocol_signature = self.compute_protocol_signature(protocol_text)
        
        document = f"""
        Problem Type: {problem_type}
        Error: {error_msg}
        Problematic Bytes: {problematic_bytes or 'N/A'}
        Byte Position: {byte_position or 'N/A'}
        Solution: {solution}
        Device Type: {device_type or 'Unknown'}
        Success: {success}
        """
        
        metadata = {
            "protocol_signature": protocol_signature,
            "problem_type": problem_type,
            "error_message": error_msg[:500],  # Truncate for metadata
            "solution_applied": solution[:500],
            "problematic_bytes": problematic_bytes or "",
            "byte_position": byte_position or -1,
            "device_type": device_type or "",
            "success": success,
            "created_at": datetime.utcnow().isoformat()
        }
        
        self._collection.add(
            ids=[experience_id],
            documents=[document],
            metadatas=[metadata]
        )
        
        logger.info(
            "Experience stored",
            experience_id=experience_id,
            problem_type=problem_type,
            success=success
        )
        
        return experience_id
    
    def find_similar_experiences(
        self,
        protocol_text: str,
        problem_type: Optional[str] = None,
        limit: int = 5
    ) -> List[dict]:
        """
        Find similar experiences based on protocol text.
        Used in SENSE phase to retrieve relevant past experiences.
        """
        query_text = f"Protocol: {protocol_text[:1000]}"
        if problem_type:
            query_text += f" Problem: {problem_type}"
        
        where_filter = None
        if problem_type:
            where_filter = {"problem_type": problem_type}
        
        results = self._collection.query(
            query_texts=[query_text],
            n_results=limit,
            where=where_filter,
            include=["documents", "metadatas", "distances"]
        )
        
        experiences = []
        if results and results['ids'] and results['ids'][0]:
            for i, exp_id in enumerate(results['ids'][0]):
                exp = {
                    "id": exp_id,
                    "distance": results['distances'][0][i] if results['distances'] else None,
                    "metadata": results['metadatas'][0][i] if results['metadatas'] else {},
                    "document": results['documents'][0][i] if results['documents'] else ""
                }
                experiences.append(exp)
        
        logger.debug(
            "Found similar experiences",
            count=len(experiences),
            problem_type=problem_type
        )
        
        return experiences
    
    def get_successful_solutions_for_signature(
        self,
        protocol_signature: str,
        limit: int = 3
    ) -> List[dict]:
        """
        Get successful solutions for a specific protocol signature.
        """
        results = self._collection.query(
            query_texts=[f"Protocol signature: {protocol_signature}"],
            n_results=limit,
            where={"success": True},
            include=["metadatas"]
        )
        
        solutions = []
        if results and results['metadatas'] and results['metadatas'][0]:
            solutions = results['metadatas'][0]
        
        return solutions
    
    def format_experiences_for_context(self, experiences: List[dict]) -> str:
        """
        Format experiences into a context string for the LLM.
        """
        if not experiences:
            return "No previous relevant experiences found."
        
        context_parts = ["## Relevant Past Experiences:\n"]
        
        for i, exp in enumerate(experiences, 1):
            meta = exp.get("metadata", {})
            context_parts.append(f"""
### Experience {i}:
- Problem Type: {meta.get('problem_type', 'Unknown')}
- Error: {meta.get('error_message', 'N/A')}
- Problematic Bytes: {meta.get('problematic_bytes', 'N/A')}
- Byte Position: {meta.get('byte_position', 'N/A')}
- Solution Applied: {meta.get('solution_applied', 'N/A')}
- Was Successful: {meta.get('success', False)}
""")
        
        return "\n".join(context_parts)


_store: Optional[ExperienceStore] = None


def get_experience_store() -> ExperienceStore:
    """Get or create the singleton experience store."""
    global _store
    if _store is None:
        _store = ExperienceStore()
    return _store
