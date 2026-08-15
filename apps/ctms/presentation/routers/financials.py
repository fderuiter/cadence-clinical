from fastapi import APIRouter, Depends, HTTPException, status

from apps.ctms.adapters.repositories import (
    SQLAlchemCTMSDelegationRepository,
    SQLAlchemyFinancialsRepository,
    get_ctms_repository,
    get_financials_repository,
)
from apps.ctms.application.financials_service import FinancialsService
from apps.ctms.presentation.dtos import (
    FinancialInvoiceCreate,
    FinancialInvoiceResponse,
    ProcedurePaymentGridCreate,
    ProcedurePaymentGridResponse,
    VisitPayableCalculationResponse,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter(prefix="/api/v1/ctms/financials", tags=["CTMS Financials"])


def get_financials_service(
    financials_repo: SQLAlchemyFinancialsRepository = Depends(
        get_financials_repository
    ),
    doa_repo: SQLAlchemCTMSDelegationRepository = Depends(get_ctms_repository),
) -> FinancialsService:
    return FinancialsService(financials_repo=financials_repo, doa_repo=doa_repo)


@router.post(
    "/procedure-grids",
    response_model=ProcedurePaymentGridResponse,
    status_code=status.HTTP_201_CREATED,
)
async def add_procedure_payment_grid(
    payload: ProcedurePaymentGridCreate,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> ProcedurePaymentGridResponse:
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    entity = await service.add_procedure_to_grid(
        grant_id=payload.grant_id,
        visit_name=payload.visit_name,
        procedure_code=payload.procedure_code,
        procedure_name=payload.procedure_name,
        base_amount=payload.base_amount,
        overhead_percentage=payload.overhead_percentage,
        withholding_percentage=payload.withholding_percentage,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Procedure payment grid setup",
    )
    return ProcedurePaymentGridResponse(
        id=entity.id or "",
        grant_id=entity.grant_id,
        visit_name=entity.visit_name,
        procedure_code=entity.procedure_code,
        procedure_name=entity.procedure_name,
        base_amount=entity.base_amount,
        overhead_percentage=entity.overhead_percentage,
        withholding_percentage=entity.withholding_percentage,
        is_active=entity.is_active,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/procedure-grids", response_model=list[ProcedurePaymentGridResponse])
async def list_procedure_grids(
    grant_id: str,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> list[ProcedurePaymentGridResponse]:
    entities = await service.list_procedure_grids(grant_id)
    return [
        ProcedurePaymentGridResponse(
            id=e.id or "",
            grant_id=e.grant_id,
            visit_name=e.visit_name,
            procedure_code=e.procedure_code,
            procedure_name=e.procedure_name,
            base_amount=e.base_amount,
            overhead_percentage=e.overhead_percentage,
            withholding_percentage=e.withholding_percentage,
            is_active=e.is_active,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.get(
    "/grants/{grant_id}/calculate-visit", response_model=VisitPayableCalculationResponse
)
async def calculate_visit_payable(
    grant_id: str,
    visit_name: str,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> VisitPayableCalculationResponse:
    calc = await service.calculate_visit_payable(grant_id, visit_name)
    return VisitPayableCalculationResponse(
        gross_amount=calc["gross_amount"],
        withholding_amount=calc["withholding_amount"],
        net_amount=calc["net_amount"],
    )


@router.post(
    "/invoices",
    response_model=FinancialInvoiceResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_batch_invoice(
    payload: FinancialInvoiceCreate,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> FinancialInvoiceResponse:
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    entity = await service.create_batch_invoice(
        study_id=payload.study_id,
        site_id=payload.site_id,
        grant_id=payload.grant_id,
        invoice_type=payload.invoice_type,
        gross_amount=payload.gross_amount,
        withholding_amount=payload.withholding_amount,
        currency=payload.currency,
        payable_ids=payload.payable_ids,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Batch invoice generation",
    )
    return FinancialInvoiceResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        grant_id=entity.grant_id,
        invoice_number=entity.invoice_number,
        invoice_type=entity.invoice_type,
        gross_amount=entity.gross_amount,
        withholding_amount=entity.withholding_amount,
        net_amount=entity.net_amount,
        currency=entity.currency,
        status=entity.status,
        payable_ids=entity.payable_ids,
        approved_by=entity.approved_by,
        approved_at=entity.approved_at,
        disbursed_at=entity.disbursed_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post("/invoices/{invoice_id}/approve", response_model=FinancialInvoiceResponse)
async def approve_and_disburse_invoice(
    invoice_id: str,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> FinancialInvoiceResponse:
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    try:
        entity = await service.approve_and_disburse_invoice(
            invoice_id=invoice_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "Invoice approval and disbursement",
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return FinancialInvoiceResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        grant_id=entity.grant_id,
        invoice_number=entity.invoice_number,
        invoice_type=entity.invoice_type,
        gross_amount=entity.gross_amount,
        withholding_amount=entity.withholding_amount,
        net_amount=entity.net_amount,
        currency=entity.currency,
        status=entity.status,
        payable_ids=entity.payable_ids,
        approved_by=entity.approved_by,
        approved_at=entity.approved_at,
        disbursed_at=entity.disbursed_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/invoices", response_model=list[FinancialInvoiceResponse])
async def list_invoices(
    study_id: str,
    site_id: str | None = None,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> list[FinancialInvoiceResponse]:
    entities = await service.list_invoices(study_id, site_id)
    return [
        FinancialInvoiceResponse(
            id=e.id or "",
            study_id=e.study_id,
            site_id=e.site_id,
            grant_id=e.grant_id,
            invoice_number=e.invoice_number,
            invoice_type=e.invoice_type,
            gross_amount=e.gross_amount,
            withholding_amount=e.withholding_amount,
            net_amount=e.net_amount,
            currency=e.currency,
            status=e.status,
            payable_ids=e.payable_ids,
            approved_by=e.approved_by,
            approved_at=e.approved_at,
            disbursed_at=e.disbursed_at,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]
