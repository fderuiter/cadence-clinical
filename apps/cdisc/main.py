import os

from fastapi import FastAPI, HTTPException

from apps.cdisc.adae import derive_adae
from apps.cdisc.adsl import derive_adsl
from apps.cdisc.advs import derive_advs
from apps.cdisc.deid import deidentify_export_data, scrub_error_message
from apps.cdisc.extractors import (
    extract_ae,
    extract_dm,
    extract_lb,
    extract_mh,
    extract_vs,
    map_cm,
)
from apps.cdisc.migration_rules import reconcile_observations
from apps.cdisc.models import ADaMRequest, BundleRequest, SDTMRequest
from apps.cdisc.serializer import serialize_to_dataset_json
from apps.cdisc.validator import DatasetJSONValidationError, validate_dataset_json

app = FastAPI(
    title="CDISC Isolated Sidecar Microservice",
    description="Synchronous CDISC mapping service using Pydantic JSON DTOs",
    version="1.0.0",
)


@app.post("/api/v1/cdisc/sdtm/{domain}")
async def map_sdtm(domain: str, req: SDTMRequest):
    dom_upper = domain.strip().upper()
    valid_domains = {"DM", "AE", "VS", "LB", "MH", "CM"}
    if dom_upper not in valid_domains:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported SDTM domain: '{domain}'. Must be one of {sorted(list(valid_domains))}",
        )

    # Reconcile in-memory
    reconciled_obs = reconcile_observations(
        req.observations, req.migration_rules, req.target_version
    )

    # Perform SDTM extraction
    records = []
    supp_records = []
    if dom_upper == "DM":
        records = extract_dm(req.subjects, reconciled_obs)
    elif dom_upper == "AE":
        records, supp_records = extract_ae(req.subjects, reconciled_obs)
    elif dom_upper == "VS":
        records, supp_records = extract_vs(req.subjects, reconciled_obs)
    elif dom_upper == "LB":
        records, supp_records = extract_lb(req.subjects, reconciled_obs)
    elif dom_upper == "MH":
        records, supp_records = extract_mh(req.subjects, reconciled_obs)
    elif dom_upper == "CM":
        records = map_cm(req.subjects, req.visits, reconciled_obs)

    for r in records:
        if "DOMAIN" not in r:
            r["DOMAIN"] = dom_upper

    export_data = {dom_upper: records}
    if supp_records:
        export_data[f"SUPP{dom_upper}"] = supp_records

    # Apply de-identification
    salt = os.getenv("BIOSTAT_EXPORT_SALT", "secure-clinical-salt-98765")  # nosec
    export_data = deidentify_export_data(export_data, salt)

    # Serialize & Validate
    try:
        dataset_json = serialize_to_dataset_json(
            data=export_data, study_id=req.study_id
        )
        validate_dataset_json(dataset_json)
        return dataset_json.model_dump()
    except DatasetJSONValidationError as e:
        scrubbed_msg = scrub_error_message(str(e))
        raise HTTPException(
            status_code=422,
            detail=f"Dataset-JSON validation failed: {scrubbed_msg}",
        )
    except Exception as e:
        scrubbed_msg = scrub_error_message(str(e))
        raise HTTPException(
            status_code=500, detail=f"Export execution failed: {scrubbed_msg}"
        )


@app.post("/api/v1/cdisc/adam/{dataset}")
async def map_adam(dataset: str, req: ADaMRequest):
    ds_upper = dataset.strip().upper()
    valid_datasets = {"ADSL", "ADAE", "ADVS"}
    if ds_upper not in valid_datasets:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported ADaM dataset: '{dataset}'. Must be one of {sorted(list(valid_datasets))}",
        )

    # Reconcile in-memory
    reconciled_obs = reconcile_observations(
        req.observations, req.migration_rules, req.target_version
    )

    # Derive analysis records
    if ds_upper == "ADSL":
        records = derive_adsl(req.subjects, reconciled_obs)
    elif ds_upper == "ADAE":
        adsl_recs = derive_adsl(req.subjects, reconciled_obs)
        ae_recs, _ = extract_ae(req.subjects, reconciled_obs)
        records = derive_adae(adsl_recs, ae_recs)
        for r in records:
            if "AEDECOD" not in r or r["AEDECOD"] is None:
                r["AEDECOD"] = r.get("AETERM", "")
    elif ds_upper == "ADVS":
        adsl_recs = derive_adsl(req.subjects, reconciled_obs)
        vs_recs, _ = extract_vs(req.subjects, reconciled_obs)
        records = derive_advs(adsl_recs, vs_recs)

    # Apply de-identification
    salt = os.getenv("BIOSTAT_EXPORT_SALT", "secure-clinical-salt-98765")  # nosec
    deidentified_records = deidentify_export_data(records, salt)

    # Serialize & Validate
    try:
        dataset_json = serialize_to_dataset_json(
            data={ds_upper: deidentified_records}, study_id=req.study_id
        )
        validate_dataset_json(dataset_json)
        return dataset_json.model_dump()
    except DatasetJSONValidationError as e:
        scrubbed_msg = scrub_error_message(str(e))
        raise HTTPException(
            status_code=422,
            detail=f"Dataset-JSON validation failed: {scrubbed_msg}",
        )
    except Exception as e:
        scrubbed_msg = scrub_error_message(str(e))
        raise HTTPException(
            status_code=500, detail=f"Export execution failed: {scrubbed_msg}"
        )


@app.post("/api/v1/cdisc/bundle")
async def map_bundle(req: BundleRequest):
    # Reconcile in-memory
    reconciled_obs = reconcile_observations(
        req.observations, req.migration_rules, req.target_version
    )

    bundle_data = {}

    # SDTM
    for dom in ["DM", "AE", "VS", "LB", "MH", "CM"]:
        records = []
        supp_records = []
        if dom == "DM":
            records = extract_dm(req.subjects, reconciled_obs)
        elif dom == "AE":
            records, supp_records = extract_ae(req.subjects, reconciled_obs)
        elif dom == "VS":
            records, supp_records = extract_vs(req.subjects, reconciled_obs)
        elif dom == "LB":
            records, supp_records = extract_lb(req.subjects, reconciled_obs)
        elif dom == "MH":
            records, supp_records = extract_mh(req.subjects, reconciled_obs)
        elif dom == "CM":
            records = map_cm(req.subjects, req.visits, reconciled_obs)

        for r in records:
            if "DOMAIN" not in r:
                r["DOMAIN"] = dom

        if records:
            bundle_data[dom] = records
        if supp_records:
            bundle_data[f"SUPP{dom}"] = supp_records

    # ADaM
    for ds in ["ADSL", "ADAE", "ADVS"]:
        records = []
        if ds == "ADSL":
            records = derive_adsl(req.subjects, reconciled_obs)
        elif ds == "ADAE":
            adsl_recs = derive_adsl(req.subjects, reconciled_obs)
            ae_recs, _ = extract_ae(req.subjects, reconciled_obs)
            records = derive_adae(adsl_recs, ae_recs)
            for r in records:
                if "AEDECOD" not in r or r["AEDECOD"] is None:
                    r["AEDECOD"] = r.get("AETERM", "")
        elif ds == "ADVS":
            adsl_recs = derive_adsl(req.subjects, reconciled_obs)
            vs_recs, _ = extract_vs(req.subjects, reconciled_obs)
            records = derive_advs(adsl_recs, vs_recs)

        if records:
            bundle_data[ds] = records

    if not bundle_data:
        raise HTTPException(
            status_code=404, detail="No biostat records found for the given study."
        )

    # Apply de-identification
    salt = os.getenv("BIOSTAT_EXPORT_SALT", "secure-clinical-salt-98765")  # nosec
    bundle_data = deidentify_export_data(bundle_data, salt)

    # Serialize & Validate
    try:
        dataset_json = serialize_to_dataset_json(
            data=bundle_data, study_id=req.study_id
        )
        validate_dataset_json(dataset_json)
        return dataset_json.model_dump()
    except DatasetJSONValidationError as e:
        scrubbed_msg = scrub_error_message(str(e))
        raise HTTPException(
            status_code=422,
            detail=f"Dataset-JSON validation failed: {scrubbed_msg}",
        )
    except Exception as e:
        scrubbed_msg = scrub_error_message(str(e))
        raise HTTPException(
            status_code=500, detail=f"Export execution failed: {scrubbed_msg}"
        )
