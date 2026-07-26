FROM python:3.12-slim AS runtime

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PYTHONPATH=/app/src \
    HF_HOME=/home/knowledge/.cache/huggingface

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 knowledge \
    && useradd --uid 10001 --gid knowledge --create-home --shell /usr/sbin/nologin knowledge
WORKDIR /app
COPY requirements.txt ./
RUN pip install --no-cache-dir --requirement requirements.txt
COPY src ./src
COPY entrypoint.sh /entrypoint.sh
RUN chmod 0555 /entrypoint.sh && chown -R knowledge:knowledge /app
USER knowledge
ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "-m", "knowledge_worker.worker_main"]
