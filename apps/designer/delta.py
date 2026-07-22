import uuid
from typing import Any, Dict, List


async def create_study_root(driver, study_id: str):
    """
    Creates a stable root node for a study.
    Requirement 1: Root-to-Value pattern.
    """
    query = """
    MERGE (s:Study {id: $study_id})
    RETURN s.id as id
    """
    async with driver.session() as session:
        result = await session.run(query, study_id=study_id)
        record = await result.single()
        return record["id"]


async def create_library_object_version(
    driver, object_id: str, new_properties: Dict[str, Any]
):
    """
    Requirement: Simplistic library objects version successfully without generating complex action nodes.
    Uses PREVIOUS_VERSION relationship.
    """
    query = """
    MATCH (old:LibraryObject {id: $object_id})
    WHERE NOT (old)<-[:PREVIOUS_VERSION]-()
    CREATE (new:LibraryObject {id: $object_id, version: coalesce(old.version, 1) + 1})
    SET new += $props
    CREATE (new)-[:PREVIOUS_VERSION]->(old)
    RETURN properties(new) as new_props
    """
    create_query = """
    MERGE (new:LibraryObject {id: $object_id})
    ON CREATE SET new.version = 1, new += $props
    RETURN properties(new) as new_props
    """
    async with driver.session() as session:
        # Check if exists
        check_query = "MATCH (n:LibraryObject {id: $object_id}) RETURN n LIMIT 1"
        check_res = await session.run(check_query, object_id=object_id)
        exists = await check_res.single()

        if exists:
            result = await session.run(query, object_id=object_id, props=new_properties)
        else:
            result = await session.run(
                create_query, object_id=object_id, props=new_properties
            )

        record = await result.single()
        return record["new_props"]


async def update_study_properties(
    driver, study_id: str, user_id: str, change_reason: str, properties: Dict[str, Any]
):
    """
    Requirement 2: Discrete action nodes connected to modified fields via BEFORE and AFTER relationships.
    """
    action_id = str(uuid.uuid4())

    query = """
    MATCH (s:Study {id: $study_id})

    // Find current active properties
    OPTIONAL MATCH (s)-[:HAS_PROPERTIES]->(old_props:StudyProperties)
    WHERE NOT (old_props)<-[:BEFORE]-()

    // Create new action node
    CREATE (a:Action {
        id: $action_id,
        user_id: $user_id,
        change_reason: $change_reason,
        timestamp: datetime()
    })

    // Create new properties node
    CREATE (new_props:StudyProperties)
    SET new_props += $properties

    // Link study to new properties
    CREATE (s)-[:HAS_PROPERTIES]->(new_props)

    // Link action to properties
    WITH a, old_props, new_props
    CREATE (a)-[:AFTER]->(new_props)

    // Link action to old properties if they exist
    WITH a, old_props
    WHERE old_props IS NOT NULL
    CREATE (a)-[:BEFORE]->(old_props)

    RETURN a.id as action_id
    """
    async with driver.session() as session:
        result = await session.run(
            query,
            study_id=study_id,
            action_id=action_id,
            user_id=user_id,
            change_reason=change_reason,
            properties=properties,
        )
        record = await result.single()
        return record["action_id"] if record else None


async def get_study_differences(
    driver, study_id: str, action_id1: str, action_id2: str
) -> List[Dict[str, Any]]:
    """
    Requirement 3: Compute human-readable field-level differences between any two version actions of a study.
    Also covers: "A study designer can retrieve a flat list of field-level differences between any two version actions of a study."
    """
    query = """
    MATCH (s:Study {id: $study_id})
    MATCH (a1:Action {id: $action_id1})-[:AFTER]->(props1:StudyProperties)
    MATCH (a2:Action {id: $action_id2})-[:AFTER]->(props2:StudyProperties)
    RETURN properties(props1) AS p1, properties(props2) AS p2, a1.timestamp AS t1, a2.timestamp AS t2
    """
    async with driver.session() as session:
        result = await session.run(
            query, study_id=study_id, action_id1=action_id1, action_id2=action_id2
        )
        record = await result.single()
        if not record:
            return []

        p1 = dict(record["p1"])
        p2 = dict(record["p2"])
        t1 = record["t1"]
        t2 = record["t2"]

        # ensure p1 is the older one
        if t1 > t2:
            p1, p2 = p2, p1

        differences = []
        all_keys = set(p1.keys()).union(set(p2.keys()))
        for key in all_keys:
            val1 = p1.get(key)
            val2 = p2.get(key)
            if val1 != val2:
                differences.append({"field": key, "old_value": val1, "new_value": val2})

        return differences
