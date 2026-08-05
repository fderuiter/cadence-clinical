import json
import pandas as pd
from pathlib import Path

def convert_excel_to_json():
    excel_path = Path("/app/docs/CDISC/Library/Terminology/SDTM_CT_2024-09-27.xlsx")
    json_path = Path("/app/docs/CDISC/Library/Terminology/SDTM_CT_2024-09-27.json")
    
    print(f"Reading Excel file: {excel_path}")
    xls = pd.ExcelFile(excel_path)
    
    data = {}
    for sheet_name in xls.sheet_names:
        df = pd.read_excel(xls, sheet_name=sheet_name)
        # Convert df to dictionary records
        records = df.to_dict(orient="records")
        data[sheet_name] = records
        
    print(f"Writing JSON file: {json_path}")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4)
        
    print("Conversion complete!")

if __name__ == "__main__":
    convert_excel_to_json()
