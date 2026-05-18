FROM mono:6.12
WORKDIR /src
COPY . .
RUN sed -i 's/deb.debian.org/archive.debian.org/g' /etc/apt/sources.list && \
    sed -i '/buster-updates/d' /etc/apt/sources.list && \
    sed -i 's|security.debian.org/debian-security|archive.debian.org/debian-security|g' /etc/apt/sources.list && \
    apt-get update && apt-get install -y --no-install-recommends nuget && rm -rf /var/lib/apt/lists/*
RUN nuget restore ZavaQueueBridge.csproj -PackagesDirectory ./packages && \
    msbuild ZavaQueueBridge.csproj /p:Configuration=Release /p:OutputPath=/app/out
WORKDIR /app/out
CMD ["mono", "ZavaQueueBridge.exe"]
