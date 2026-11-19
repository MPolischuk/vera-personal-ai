# Build context: repository root.
# Coolify runs Compose against the host daemon. A bind of this file becomes a
# directory when that path is not a file on the host, and SearXNG exits.
# The build context is uploaded, so the copy stays a file.
FROM searxng/searxng:2026.11.5-3e95f6cd2

COPY docker/searxng/settings.yml /etc/searxng/settings.yml
