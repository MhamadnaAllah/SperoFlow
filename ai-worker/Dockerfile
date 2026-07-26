FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgomp1 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 app \
    && useradd --uid 10001 --gid app --create-home --shell /usr/sbin/nologin app

WORKDIR /app

COPY ai-core/requirements-base.txt /tmp/ai-core/requirements-base.txt
COPY ai-worker/requirements.txt /tmp/ai-worker/requirements.txt
RUN pip install --no-cache-dir -r /tmp/ai-worker/requirements.txt
COPY ai-core/pyproject.toml /tmp/ai-core/pyproject.toml
COPY ai-core/src /tmp/ai-core/src
RUN pip install --no-cache-dir /tmp/ai-core
COPY ai-worker/worker_main.py /app/worker_main.py
COPY ai-worker/scripts /app/scripts
COPY infrastructure/docker/entrypoint-ai.sh /entrypoint.sh
RUN chmod 0555 /entrypoint.sh && chown -R app:app /app

USER app
ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "/app/worker_main.py"]