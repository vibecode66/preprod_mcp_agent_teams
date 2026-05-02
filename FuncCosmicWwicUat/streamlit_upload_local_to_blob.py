import FuncCosmicWwicUat.streamlit_upload_local_to_blob as st
import requests
import json
import os

# =============================================================================
# Configuration
# =============================================================================
DEFAULT_FUNCTION_BASE_URL = "https://func-cosmic-wwic-uat-westus.azurewebsites.net"

# These are the NEW endpoints your Function App should expose for blob operations
UPLOAD_ROUTE = "api/upload"     # POST multipart/form-data
LIST_ROUTE   = "api/blobs"      # GET list blobs (optional)

DEFAULT_CONTAINER = "copilotmcpagentdocuments"

st.set_page_config(page_title="Blob Uploader (UAMI via Function)", layout="centered")
st.title("Upload local files to Azure Blob (via Function App using UAMI)")
st.caption("Client uploads to Function App; Function App uploads to Blob using Managed Identity (UAMI).")

with st.sidebar:
    st.header("Settings")

    function_base_url = st.text_input(
        "Function App Base URL",
        value=os.getenv("FUNCTION_BASE_URL", DEFAULT_FUNCTION_BASE_URL),
        help="Base URL only (no trailing slash). Example: https://func-...azurewebsites.net",
    )

    container = st.text_input(
        "Container",
        value=os.getenv("BLOB_CONTAINER_NAME", DEFAULT_CONTAINER),
        help="Blob container name.",
    )

    prefix = st.text_input(
        "Blob prefix (virtual folder)",
        value="",
        help="Optional: e.g. 'uploads' or 'docs/2026-04-06'",
    )

    overwrite = st.checkbox("Overwrite if exists", value=True)

    st.divider()
    list_after = st.checkbox("List blobs after upload", value=False)
    list_limit = st.slider("List limit", 1, 500, 50)

uploaded_files = st.file_uploader(
    "Choose file(s) to upload",
    type=None,
    accept_multiple_files=True
)

if uploaded_files:
    st.write(f"Selected {len(uploaded_files)} file(s).")

    if st.button("Upload", type="primary", use_container_width=True):
        upload_url = f"{function_base_url.rstrip('/')}/{UPLOAD_ROUTE}"

        for f in uploaded_files:
            params = {
                "container": container,
                "overwrite": str(overwrite).lower(),
            }
            if prefix.strip():
                params["prefix"] = prefix.strip()

            files = {
                "file": (f.name, f.getvalue(), f.type or "application/octet-stream")
            }

            with st.expander(f"Request details: {f.name}"):
                st.code(f"POST {upload_url}\nParams: {json.dumps(params, indent=2)}", language="text")

            try:
                resp = requests.post(upload_url, params=params, files=files, timeout=180)
            except Exception as e:
                st.error(f"Upload failed for {f.name}: {e}")
                continue

            if resp.status_code == 200:
                st.success(f"Uploaded: {f.name}")
                try:
                    st.json(resp.json())
                except Exception:
                    st.code(resp.text)
            else:
                st.error(f"Upload failed for {f.name} (HTTP {resp.status_code})")
                try:
                    st.json(resp.json())
                except Exception:
                    st.code(resp.text)

        if list_after:
            list_url = f"{function_base_url.rstrip('/')}/{LIST_ROUTE}"
            try:
                list_params = {"container": container, "limit": str(list_limit)}
                if prefix.strip():
                    list_params["prefix"] = prefix.strip()

                lr = requests.get(list_url, params=list_params, timeout=60)
                if lr.status_code == 200:
                    data = lr.json()
                    st.subheader("Blob listing")
                    st.json(data)
                else:
                    st.error(f"List failed (HTTP {lr.status_code})")
                    st.code(lr.text)
            except Exception as e:
                st.error(f"List failed: {e}")
else:
    st.info("Select one or more files to upload.")