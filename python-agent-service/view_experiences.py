"""Script to view all experiences stored in ChromaDB."""
import chromadb
from chromadb.config import Settings as ChromaSettings
import json
import sys
import io

# Fix Windows console encoding
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

def view_all_experiences():
    """View all experiences stored in ChromaDB."""
    
    # Connect to ChromaDB
    client = chromadb.PersistentClient(
        path="./chroma_db",
        settings=ChromaSettings(anonymized_telemetry=False)
    )
    
    # Get the collection
    try:
        collection = client.get_collection("protocol_experiences")
    except Exception as e:
        print(f"Error getting collection: {e}")
        print("Available collections:", [c.name for c in client.list_collections()])
        return
    
    # Get count
    count = collection.count()
    print(f"\n{'='*60}")
    print(f"ChromaDB Experience Store")
    print(f"{'='*60}")
    print(f"Total experiences: {count}")
    print(f"{'='*60}\n")
    
    if count == 0:
        print("No experiences stored yet.")
        return
    
    # Get all experiences
    results = collection.get(
        include=["documents", "metadatas"]
    )
    
    if not results or not results['ids']:
        print("No data found.")
        return
    
    for i, exp_id in enumerate(results['ids']):
        metadata = results['metadatas'][i] if results['metadatas'] else {}
        document = results['documents'][i] if results['documents'] else ""
        
        print(f"+{'-'*58}+")
        print(f"| Experience #{i+1}: {exp_id}")
        print(f"+{'-'*58}+")
        
        # Parse metadata
        print(f"| Created: {metadata.get('created_at', 'Unknown')}")
        print(f"| Device: {metadata.get('device_type', 'Unknown')}")
        print(f"| Problem Type: {metadata.get('problem_type', 'Unknown')}")
        print(f"| Success: {metadata.get('success', False)}")
        print(f"|")
        print(f"| Error Message:")
        error_msg = metadata.get('error_message', 'N/A')
        if len(error_msg) > 80:
            error_msg = error_msg[:80] + "..."
        print(f"|   {error_msg}")
        print(f"|")
        print(f"| Solution Applied:")
        solution = metadata.get('solution_applied', 'N/A')
        if len(solution) > 80:
            solution = solution[:80] + "..."
        print(f"|   {solution}")
        print(f"|")
        print(f"| Problematic Bytes: {metadata.get('problematic_bytes', 'N/A')}")
        print(f"| Byte Position: {metadata.get('byte_position', 'N/A')}")
        print(f"| Protocol Signature: {metadata.get('protocol_signature', 'N/A')}")
        print(f"+{'-'*58}+")
        print()
    
    # Summary
    print(f"\n{'='*60}")
    print("Summary")
    print(f"{'='*60}")
    
    successes = sum(1 for m in results['metadatas'] if m.get('success', False))
    failures = count - successes
    
    print(f"Successful experiences: {successes}")
    print(f"Failed experiences: {failures}")
    
    # Problem types
    problem_types = {}
    for m in results['metadatas']:
        pt = m.get('problem_type', 'unknown')
        problem_types[pt] = problem_types.get(pt, 0) + 1
    
    print(f"\nProblem Types:")
    for pt, cnt in problem_types.items():
        print(f"   - {pt}: {cnt}")
    
    # Device types
    device_types = {}
    for m in results['metadatas']:
        dt = m.get('device_type', 'unknown') or 'unknown'
        device_types[dt] = device_types.get(dt, 0) + 1
    
    print(f"\nDevice Types:")
    for dt, cnt in device_types.items():
        print(f"   - {dt}: {cnt}")


if __name__ == "__main__":
    view_all_experiences()
