"""RTSM Supply Chain and Inventory Management.

Provides domain logic, pure-Python checks, and a transactional service layer
for kit dispensation against site inventory and threshold-triggered resupply events.
"""

from sqlalchemy.future import select

from apps.execution.database.models import KitDispensation, ResupplyEvent, SiteInventory


class SiteInventoryNotFoundError(Exception):
    """Raised when no inventory record exists for the given site and kit combination."""

    pass


class InsufficientStockError(Exception):
    """Raised when the requested dispensation quantity exceeds on-hand site inventory."""

    pass


def evaluate_resupply(on_hand_qty: int, reorder_threshold: int) -> bool:
    """Pure-Python stateless check for inventory threshold breach.

    Returns True when on_hand_qty is at or below the reorder threshold.
    Operates strictly on blinded quantities and does not reference treatment arms.
    """
    return on_hand_qty <= reorder_threshold


async def dispense_kit_transaction(
    session,
    study_id: str,
    site_id: str,
    subject_id: str,
    visit_id: str,
    kit_id: str,
    quantity: int,
) -> bool:
    """Performs transactional kit dispensation and site inventory updates.

    1. Acquires a pessimistic row lock on the SiteInventory row via select(...).with_for_update().
    2. Validates stock levels, raising InsufficientStockError if quantity exceeds on-hand quantity.
    3. Decrements on-hand inventory and creates a KitDispensation record in the session.
    4. Evaluates resupply conditions via evaluate_resupply.
    5. If resupply is signaled and no PENDING resupply event exists for the (study, site, kit),
       creates a ResupplyEvent with status "PENDING" and flags the inventory.
    6. Returns a boolean indicating whether a new ResupplyEvent was created.
    """
    # 1. Load the SiteInventory row for (site_id, kit_id) with pessimistic row locking
    stmt = (
        select(SiteInventory)
        .where(SiteInventory.site_id == site_id, SiteInventory.kit_id == kit_id)
        .with_for_update()
    )
    result = await session.execute(stmt)
    inventory = result.scalars().first()

    if not inventory:
        raise SiteInventoryNotFoundError(
            f"No inventory record found for site {site_id} and kit {kit_id}."
        )

    # 2. Re-check on_hand_qty >= requested quantity
    if inventory.on_hand_qty < quantity:
        raise InsufficientStockError(
            f"Insufficient stock for kit {kit_id} at site {site_id}: "
            f"requested {quantity}, on-hand {inventory.on_hand_qty}."
        )

    # 3. Add a KitDispensation record and decrement on_hand_qty in the same session
    dispensation = KitDispensation(
        study_id=study_id,
        subject_id=subject_id,
        kit_id=kit_id,
        site_id=site_id,
        visit_id=visit_id,
        quantity=quantity,
    )
    session.add(dispensation)

    inventory.on_hand_qty -= quantity

    # 4. After decrementing, call evaluate_resupply
    new_resupply_created = False
    if evaluate_resupply(inventory.on_hand_qty, inventory.reorder_threshold):
        inventory.resupply_signal = True

        # Check for existing PENDING ResupplyEvent (dedup check)
        stmt_pending = select(ResupplyEvent).where(
            ResupplyEvent.study_id == study_id,
            ResupplyEvent.site_id == site_id,
            ResupplyEvent.kit_id == kit_id,
            ResupplyEvent.status == "PENDING",
            ResupplyEvent.is_deleted.is_(False),
        )
        res_pending = await session.execute(stmt_pending)
        existing_event = res_pending.scalars().first()

        if not existing_event:
            # Create a new PENDING ResupplyEvent
            # Use 20 as standard default requested quantity as indicated by requirements/tests
            requested_qty = 20
            new_event = ResupplyEvent(
                study_id=study_id,
                site_id=site_id,
                kit_id=kit_id,
                requested_qty=requested_qty,
                status="PENDING",
            )
            session.add(new_event)
            new_resupply_created = True

    return new_resupply_created
