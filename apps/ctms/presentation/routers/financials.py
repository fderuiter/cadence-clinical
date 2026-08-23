from fastapi import APIRouter, Depends, HTTPException, status

from apps.ctms.adapters.repositories import (
    SQLAlchemCTMSDelegationRepository,
    SQLAlchemyFinancialsRepository,
    get_ctms_repository,
    get_financials_repository,
)
from apps.ctms.application.financials_service import FinancialsService
from apps.ctms.presentation.dtos import (
    ApplyTemplateRequest,
    BudgetAdjustmentCreate,
    BudgetAdjustmentResponse,
    ContractAmendmentCreate,
    ContractAmendmentResponse,
    CurrencyConvertRequest,
    CurrencyConvertResponse,
    CurrencyExchangeRateCreate,
    CurrencyExchangeRateResponse,
    FinancialInvoiceCreate,
    FinancialInvoiceResponse,
    ProcedurePaymentGridCreate,
    ProcedurePaymentGridResponse,
    SiteBudgetTemplateCreate,
    SiteBudgetTemplateResponse,
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


# ==========================================
# 1. Site Budget Templates (Multi-Tier)
# ==========================================


@router.post(
    "/templates",
    response_model=SiteBudgetTemplateResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_budget_template(
    payload: SiteBudgetTemplateCreate,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> SiteBudgetTemplateResponse:
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    entity = await service.create_budget_template(
        template_name=payload.template_name,
        study_id=payload.study_id,
        tier_level=payload.tier_level,
        currency=payload.currency,
        overhead_percentage=payload.overhead_percentage,
        withholding_percentage=payload.withholding_percentage,
        fixed_startup_fee=payload.fixed_startup_fee,
        fixed_closeout_fee=payload.fixed_closeout_fee,
        irb_fee=payload.irb_fee,
        patient_stipend_amount=payload.patient_stipend_amount,
        travel_reimbursement_rate=payload.travel_reimbursement_rate,
        fixed_fees=payload.fixed_fees,
        procedure_items=payload.procedure_items,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Created site budget template",
    )
    return SiteBudgetTemplateResponse(
        id=entity.id or "",
        template_name=entity.template_name,
        study_id=entity.study_id,
        tier_level=entity.tier_level,
        currency=entity.currency,
        overhead_percentage=entity.overhead_percentage,
        withholding_percentage=entity.withholding_percentage,
        fixed_startup_fee=entity.fixed_startup_fee,
        fixed_closeout_fee=entity.fixed_closeout_fee,
        irb_fee=entity.irb_fee,
        patient_stipend_amount=entity.patient_stipend_amount,
        travel_reimbursement_rate=entity.travel_reimbursement_rate,
        fixed_fees=entity.fixed_fees,
        procedure_items=entity.procedure_items,
        is_active=entity.is_active,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/templates", response_model=list[SiteBudgetTemplateResponse])
async def list_budget_templates(
    study_id: str | None = None,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> list[SiteBudgetTemplateResponse]:
    entities = await service.list_budget_templates(study_id=study_id)
    return [
        SiteBudgetTemplateResponse(
            id=e.id or "",
            template_name=e.template_name,
            study_id=e.study_id,
            tier_level=e.tier_level,
            currency=e.currency,
            overhead_percentage=e.overhead_percentage,
            withholding_percentage=e.withholding_percentage,
            fixed_startup_fee=e.fixed_startup_fee,
            fixed_closeout_fee=e.fixed_closeout_fee,
            irb_fee=e.irb_fee,
            patient_stipend_amount=e.patient_stipend_amount,
            travel_reimbursement_rate=e.travel_reimbursement_rate,
            fixed_fees=e.fixed_fees,
            procedure_items=e.procedure_items,
            is_active=e.is_active,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.get("/templates/{template_id}", response_model=SiteBudgetTemplateResponse)
async def get_budget_template(
    template_id: str,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> SiteBudgetTemplateResponse:
    entity = await service.get_budget_template(template_id)
    if not entity:
        raise HTTPException(status_code=404, detail="Budget template not found")
    return SiteBudgetTemplateResponse(
        id=entity.id or "",
        template_name=entity.template_name,
        study_id=entity.study_id,
        tier_level=entity.tier_level,
        currency=entity.currency,
        overhead_percentage=entity.overhead_percentage,
        withholding_percentage=entity.withholding_percentage,
        fixed_startup_fee=entity.fixed_startup_fee,
        fixed_closeout_fee=entity.fixed_closeout_fee,
        irb_fee=entity.irb_fee,
        patient_stipend_amount=entity.patient_stipend_amount,
        travel_reimbursement_rate=entity.travel_reimbursement_rate,
        fixed_fees=entity.fixed_fees,
        procedure_items=entity.procedure_items,
        is_active=entity.is_active,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post(
    "/grants/{grant_id}/apply-template",
    response_model=list[ProcedurePaymentGridResponse],
    status_code=status.HTTP_201_CREATED,
)
async def apply_template_to_grant(
    grant_id: str,
    payload: ApplyTemplateRequest,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> list[ProcedurePaymentGridResponse]:
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    try:
        entities = await service.apply_template_to_grant(
            grant_id=grant_id,
            template_id=payload.template_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or f"Applied template {payload.template_id}",
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))

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
            activity_id=e.activity_id,
            form_id=e.form_id,
            patient_stipend=e.patient_stipend,
            travel_reimbursement=e.travel_reimbursement,
            is_active=e.is_active,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


# ==========================================
# 2. Procedure Payment Grid
# ==========================================


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
        activity_id=payload.activity_id,
        form_id=payload.form_id,
        patient_stipend=payload.patient_stipend,
        travel_reimbursement=payload.travel_reimbursement,
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
        activity_id=entity.activity_id,
        form_id=entity.form_id,
        patient_stipend=entity.patient_stipend,
        travel_reimbursement=entity.travel_reimbursement,
        is_active=entity.is_active,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/procedure-grids", response_model=list[ProcedurePaymentGridResponse])
async def list_procedure_grids(
    grant_id: str,
    is_active_only: bool = False,
    version_index: int | None = None,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> list[ProcedurePaymentGridResponse]:
    entities = await service.list_procedure_grids(
        grant_id=grant_id,
        is_active_only=is_active_only,
        version_index=version_index,
    )
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
            activity_id=e.activity_id,
            form_id=e.form_id,
            patient_stipend=e.patient_stipend,
            travel_reimbursement=e.travel_reimbursement,
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
    version_index: int | None = None,
    currency: str = "USD",
    target_currency: str | None = None,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> VisitPayableCalculationResponse:
    calc = await service.calculate_visit_payable(
        grant_id=grant_id,
        visit_name=visit_name,
        version_index=version_index,
        grant_currency=currency,
        target_currency=target_currency,
    )
    return VisitPayableCalculationResponse(
        gross_amount=calc["gross_amount"],
        withholding_amount=calc["withholding_amount"],
        net_amount=calc["net_amount"],
        visit_name=calc.get("visit_name"),
        procedures_count=calc.get("procedures_count", 0),
        base_amount=calc.get("base_amount", 0.0),
        overhead_amount=calc.get("overhead_amount", 0.0),
        patient_stipend_total=calc.get("patient_stipend_total", 0.0),
        travel_reimbursement_total=calc.get("travel_reimbursement_total", 0.0),
        currency=calc.get("currency", "USD"),
        target_currency=calc.get("target_currency"),
        exchange_rate=calc.get("exchange_rate", 1.0),
        converted_net_amount=calc.get("converted_net_amount"),
        version_index=calc.get("version_index"),
    )


# ==========================================
# 3. Contract Amendments & Versioning
# ==========================================


@router.post(
    "/grants/{grant_id}/amend",
    response_model=ContractAmendmentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def execute_contract_amendment(
    grant_id: str,
    payload: ContractAmendmentCreate,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> ContractAmendmentResponse:
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    entity = await service.execute_contract_amendment(
        grant_id=grant_id,
        summary_of_changes=payload.summary_of_changes,
        revised_budget=payload.revised_budget,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or payload.summary_of_changes,
        updated_procedures=payload.updated_procedures,
        signature_manifestation=payload.signature_manifestation,
    )
    return ContractAmendmentResponse(
        id=entity.id or "",
        grant_id=entity.grant_id,
        amendment_number=entity.amendment_number,
        effective_date=entity.effective_date,
        previous_budget=entity.previous_budget,
        revised_budget=entity.revised_budget,
        previous_version_index=entity.previous_version_index,
        new_version_index=entity.new_version_index,
        summary_of_changes=entity.summary_of_changes,
        status=entity.status,
        approved_by=entity.approved_by,
        approved_at=entity.approved_at,
        signature_manifestation=entity.signature_manifestation,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get(
    "/grants/{grant_id}/amendments", response_model=list[ContractAmendmentResponse]
)
async def list_grant_amendments(
    grant_id: str,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> list[ContractAmendmentResponse]:
    entities = await service.list_grant_amendments(grant_id)
    return [
        ContractAmendmentResponse(
            id=e.id or "",
            grant_id=e.grant_id,
            amendment_number=e.amendment_number,
            effective_date=e.effective_date,
            previous_budget=e.previous_budget,
            revised_budget=e.revised_budget,
            previous_version_index=e.previous_version_index,
            new_version_index=e.new_version_index,
            summary_of_changes=e.summary_of_changes,
            status=e.status,
            approved_by=e.approved_by,
            approved_at=e.approved_at,
            signature_manifestation=e.signature_manifestation,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.get("/amendments/{amendment_id}", response_model=ContractAmendmentResponse)
async def get_grant_amendment(
    amendment_id: str,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> ContractAmendmentResponse:
    entity = await service.get_grant_amendment(amendment_id)
    if not entity:
        raise HTTPException(status_code=404, detail="Contract amendment not found")
    return ContractAmendmentResponse(
        id=entity.id or "",
        grant_id=entity.grant_id,
        amendment_number=entity.amendment_number,
        effective_date=entity.effective_date,
        previous_budget=entity.previous_budget,
        revised_budget=entity.revised_budget,
        previous_version_index=entity.previous_version_index,
        new_version_index=entity.new_version_index,
        summary_of_changes=entity.summary_of_changes,
        status=entity.status,
        approved_by=entity.approved_by,
        approved_at=entity.approved_at,
        signature_manifestation=entity.signature_manifestation,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


# ==========================================
# 4. Currency Exchange Rates & Conversions
# ==========================================


@router.post(
    "/exchange-rates",
    response_model=CurrencyExchangeRateResponse,
    status_code=status.HTTP_201_CREATED,
)
async def set_exchange_rate(
    payload: CurrencyExchangeRateCreate,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> CurrencyExchangeRateResponse:
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    entity = await service.set_exchange_rate(
        base_currency=payload.base_currency,
        target_currency=payload.target_currency,
        rate=payload.rate,
        expiry_date=payload.expiry_date,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Exchange rate configuration",
    )
    return CurrencyExchangeRateResponse(
        id=entity.id or "",
        base_currency=entity.base_currency,
        target_currency=entity.target_currency,
        rate=entity.rate,
        effective_date=entity.effective_date,
        expiry_date=entity.expiry_date,
        is_active=entity.is_active,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/exchange-rates", response_model=list[CurrencyExchangeRateResponse])
async def list_exchange_rates(
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> list[CurrencyExchangeRateResponse]:
    entities = await service.list_exchange_rates()
    return [
        CurrencyExchangeRateResponse(
            id=e.id or "",
            base_currency=e.base_currency,
            target_currency=e.target_currency,
            rate=e.rate,
            effective_date=e.effective_date,
            expiry_date=e.expiry_date,
            is_active=e.is_active,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.post("/convert", response_model=CurrencyConvertResponse)
async def convert_currency(
    payload: CurrencyConvertRequest,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> CurrencyConvertResponse:
    res = await service.convert_currency(
        amount=payload.amount,
        from_currency=payload.from_currency,
        to_currency=payload.to_currency,
    )
    return CurrencyConvertResponse(
        original_amount=res["original_amount"],
        from_currency=res["from_currency"],
        to_currency=res["to_currency"],
        exchange_rate=res["exchange_rate"],
        converted_amount=res["converted_amount"],
    )


# ==========================================
# 5. Budget Adjustments (Passthroughs & Penalties)
# ==========================================


@router.post(
    "/adjustments",
    response_model=BudgetAdjustmentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_budget_adjustment(
    payload: BudgetAdjustmentCreate,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> BudgetAdjustmentResponse:
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    entity = await service.create_budget_adjustment(
        grant_id=payload.grant_id,
        site_id=payload.site_id,
        study_id=payload.study_id,
        adjustment_type=payload.adjustment_type,
        description=payload.description,
        amount=payload.amount,
        currency=payload.currency,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or payload.description,
    )
    return BudgetAdjustmentResponse(
        id=entity.id or "",
        grant_id=entity.grant_id,
        site_id=entity.site_id,
        study_id=entity.study_id,
        adjustment_type=entity.adjustment_type,
        description=entity.description,
        amount=entity.amount,
        currency=entity.currency,
        status=entity.status,
        approved_by=entity.approved_by,
        approved_at=entity.approved_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/adjustments", response_model=list[BudgetAdjustmentResponse])
async def list_budget_adjustments(
    grant_id: str | None = None,
    study_id: str | None = None,
    site_id: str | None = None,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> list[BudgetAdjustmentResponse]:
    entities = await service.list_budget_adjustments(
        grant_id=grant_id, study_id=study_id, site_id=site_id
    )
    return [
        BudgetAdjustmentResponse(
            id=e.id or "",
            grant_id=e.grant_id,
            site_id=e.site_id,
            study_id=e.study_id,
            adjustment_type=e.adjustment_type,
            description=e.description,
            amount=e.amount,
            currency=e.currency,
            status=e.status,
            approved_by=e.approved_by,
            approved_at=e.approved_at,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.post(
    "/adjustments/{adjustment_id}/approve",
    response_model=BudgetAdjustmentResponse,
)
async def approve_budget_adjustment(
    adjustment_id: str,
    service: FinancialsService = Depends(get_financials_service),
    principal: Principal = Depends(get_principal),
) -> BudgetAdjustmentResponse:
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    try:
        entity = await service.approve_budget_adjustment(
            adjustment_id=adjustment_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Approved budget adjustment",
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return BudgetAdjustmentResponse(
        id=entity.id or "",
        grant_id=entity.grant_id,
        site_id=entity.site_id,
        study_id=entity.study_id,
        adjustment_type=entity.adjustment_type,
        description=entity.description,
        amount=entity.amount,
        currency=entity.currency,
        status=entity.status,
        approved_by=entity.approved_by,
        approved_at=entity.approved_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


# ==========================================
# 6. Invoices & Disbursements
# ==========================================


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
