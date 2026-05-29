FROM python:3.11-slim-bookworm AS build

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV PIP_NO_CACHE_DIR=1

RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential gcc g++ libgomp1 \
    && rm -rf /var/lib/apt/lists/*

ENV VIRTUAL_ENV=/opt/venv
RUN python -m venv "$VIRTUAL_ENV"
ENV PATH="$VIRTUAL_ENV/bin:$PATH"

WORKDIR /app/src/NemoDataAnalysisAgent/nemo

COPY src/NemoDataAnalysisAgent/pyproject.toml ../pyproject.toml
COPY src/NemoDataAnalysisAgent/nemo ./
COPY src/NemoDataAnalysisAgent/src ../src

RUN pip install --upgrade pip \
    && pip install -r requirements.txt

FROM python:3.11-slim-bookworm AS runtime

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV PATH="/opt/venv/bin:$PATH"
ENV NEMO_HOST=0.0.0.0
ENV NEMO_PORT=8088
ENV NEMO_WORKFLOW_PROFILE=fast
ENV NEMO_PUBLIC_BASE_URL=http://nemo-agent:8088

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app/src/NemoDataAnalysisAgent/nemo

COPY --from=build /opt/venv /opt/venv
COPY src/NemoDataAnalysisAgent/pyproject.toml ../pyproject.toml
COPY src/NemoDataAnalysisAgent/nemo ./
COPY src/NemoDataAnalysisAgent/src ../src

EXPOSE 8088

CMD ["sh", "-c", "workflow_file=workflow.yml; if [ \"${NEMO_WORKFLOW_PROFILE}\" = \"fast\" ]; then workflow_file=workflow-fast.yml; fi; nat a2a serve --config_file ${workflow_file} --host ${NEMO_HOST} --port ${NEMO_PORT} --public_base_url ${NEMO_PUBLIC_BASE_URL} --name nemo-data-analysis-agent"]
