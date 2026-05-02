import streamlit as st
import requests
import json
import os

DEFAULT_FUNCTION_BASE_URL = "https://func-cosmic-wwic-uat-westus.azurewebsites.net"

UPLOAD_ROUTE = "api/upload"
LIST_ROUTE = "api/blobs"

st.set_page_config(page_title="WWIC Blob Uploader", layout="wide")
st.title("WWIC: Upload local files to Blob via Azure Function (UAMI)")

with st.sidebar:
    function_base_url = st.text_input(
        "Function App Base URL",
        value=os.getenv("FUNCTION_BASE_URL", DEFAULT_FUNCTION_BASE_URL),
    )
    prefix = st.text_input("Blob prefix (optional)", value="")
    overwrite = st.checkbox("Overwrite if exists", value=True)

    st.divider()
    list_after = st.checkbox("List blobs after upload", value=False)
    list_limit = st.slider("List limit", 1, 500, 50)
    
    st.divider()
    auto_refresh = st.checkbox("Auto-refresh blob list", value=True)
    refresh_interval = st.slider("Refresh interval (seconds)", 5, 60, 10) if auto_refresh else 10

uploaded_files = st.file_uploader("Choose file(s)", accept_multiple_files=True)

blob_name_override = st.text_input(
    "Override blob name (optional, only used if uploading 1 file)",
    value="",
)

if uploaded_files:
    if st.button("Upload", type="primary", use_container_width=True):
        upload_url = f"{function_base_url.rstrip('/')}/{UPLOAD_ROUTE}"

        for f in uploaded_files:
            params = {"overwrite": str(overwrite).lower()}

            if prefix.strip():
                params["prefix"] = prefix.strip()

            if blob_name_override.strip() and len(uploaded_files) == 1:
                params["blob"] = blob_name_override.strip()

            files = {"file": (f.name, f.getvalue(), f.type or "application/octet-stream")}

            with st.expander(f"Request details: {f.name}"):
                st.code(f"POST {upload_url}\nParams: {json.dumps(params, indent=2)}", language="text")

            r = requests.post(upload_url, params=params, files=files, timeout=180)

            if r.status_code == 200:
                st.success(f"Uploaded: {f.name}")
                st.json(r.json())
            else:
                st.error(f"Upload failed (HTTP {r.status_code}) for {f.name}")
                try:
                    st.json(r.json())
                except Exception:
                    st.code(r.text)

        if list_after:
            list_url = f"{function_base_url.rstrip('/')}/{LIST_ROUTE}"
            lr = requests.get(
                list_url,
                params={"prefix": prefix.strip(), "limit": str(list_limit)},
                timeout=60,
            )

            if lr.status_code == 200:
                st.subheader("Blob listing")
                st.json(lr.json())
            else:
                st.error(f"List failed (HTTP {lr.status_code})")
                st.code(lr.text)
else:
    st.info("Select one or more files to upload.")

st.divider()

# Blob Container Explorer Section
st.subheader("📂 Blob Container Explorer")
col1, col2 = st.columns([3, 1])

with col2:
    if st.button("🔄 Refresh", use_container_width=True):
        st.rerun()

try:
    list_url = f"{function_base_url.rstrip('/')}/{LIST_ROUTE}"
    
    # Get all blobs with high limit
    lr = requests.get(
        list_url,
        params={"prefix": "", "limit": str(1000)},
        timeout=60,
    )
    
    if lr.status_code == 200:
        blobs_data = lr.json()
        
        # Handle different response formats
        if isinstance(blobs_data, dict):
            if "items" in blobs_data:
                blobs = blobs_data["items"]
            elif "blobs" in blobs_data:
                blobs = blobs_data["blobs"]
            else:
                blobs = []
        elif isinstance(blobs_data, list):
            blobs = blobs_data
        else:
            blobs = []
        
        if blobs:
            st.success(f"✅ Found {len(blobs)} blob(s) in container")
            
            # Create a formatted table
            col1, col2, col3 = st.columns([3, 1, 1])
            with col1:
                st.write("**Blob Name**")
            with col2:
                st.write("**Size**")
            with col3:
                st.write("**Modified**")
            
            st.divider()
            
            for blob in blobs:
                col1, col2, col3 = st.columns([3, 1, 1])
                
                blob_name = blob.get("name", "N/A") if isinstance(blob, dict) else str(blob)
                blob_size = blob.get("size", "N/A") if isinstance(blob, dict) else "N/A"
                blob_modified = blob.get("lastModified", "N/A") if isinstance(blob, dict) else "N/A"
                
                with col1:
                    st.code(blob_name, language="text")
                with col2:
                    if isinstance(blob_size, int):
                        st.write(f"{blob_size / 1024:.1f} KB" if blob_size > 1024 else f"{blob_size} B")
                    else:
                        st.write(str(blob_size))
                with col3:
                    st.write(str(blob_modified)[:19])
        else:
            st.warning("📭 No blobs found in container")
            
    else:
        st.error(f"❌ Failed to list blobs (HTTP {lr.status_code})")
        try:
            error_data = lr.json()
            st.json(error_data)
        except Exception:
            st.code(lr.text)
            
except requests.exceptions.RequestException as e:
    st.error(f"❌ Connection error: {str(e)}")
except Exception as e:
    st.error(f"❌ Error: {str(e)}")