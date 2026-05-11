# syntax=docker/dockerfile:1.7

# Build context is the repo root; this Dockerfile lives at src/FundingPlatform.Web/Dockerfile.
# Aspire publishes the Web project as a container resource via PublishAsDockerFile.

ARG TARGETARCH=amd64

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY FundingPlatform.slnx ./
COPY src/FundingPlatform.Application/ src/FundingPlatform.Application/
COPY src/FundingPlatform.Domain/ src/FundingPlatform.Domain/
COPY src/FundingPlatform.Infrastructure/ src/FundingPlatform.Infrastructure/
COPY src/FundingPlatform.ServiceDefaults/ src/FundingPlatform.ServiceDefaults/
COPY src/FundingPlatform.Web/ src/FundingPlatform.Web/

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/FundingPlatform.Web/FundingPlatform.Web.csproj -a "$TARGETARCH"

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/FundingPlatform.Web/FundingPlatform.Web.csproj \
      -c Release \
      -a "$TARGETARCH" \
      --no-restore \
      -o /app/publish


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Syncfusion HtmlToPdfConverter (Blink/Chromium) runtime dependencies for Ubuntu 24.04 (Noble).
# Several libs picked up the t64 suffix during the 64-bit time_t ABI transition; libgconf-2-4
# and libxss1 from the Syncfusion docs were dropped in 24.04 and Chromium no longer needs them.
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
      ca-certificates \
      fonts-liberation \
      libasound2t64 \
      libatk-bridge2.0-0t64 \
      libatk1.0-0t64 \
      libcairo2 \
      libcups2t64 \
      libdbus-1-3 \
      libdrm2 \
      libexpat1 \
      libfontconfig1 \
      libgbm1 \
      libglib2.0-0t64 \
      libgtk-3-0t64 \
      libnspr4 \
      libnss3 \
      libpango-1.0-0 \
      libpangocairo-1.0-0 \
      libx11-6 \
      libx11-xcb1 \
      libxcb1 \
      libxcomposite1 \
      libxcursor1 \
      libxdamage1 \
      libxext6 \
      libxfixes3 \
      libxi6 \
      libxkbcommon0 \
      libxrandr2 \
      libxrender1 \
      libxshmfence1 \
      libxtst6 && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Syncfusion's HtmlToPdfConverter.Net.Linux NuGet ships locales/en-US.pak under
# runtimes/linux/native/locales/, but the .NET SDK flattens runtime native assets
# at publish time so the file lands next to chrome instead of inside locales/.
# Chromium aborts with SIGTRAP when the locale resource bundle can't be loaded,
# which surfaces as Syncfusion's "Failed to launch Base!" exception on first PDF.
RUN mkdir -p /app/locales && \
    mv /app/en-US.pak /app/locales/en-US.pak && \
    mv /app/en-US.pak.info /app/locales/en-US.pak.info

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FundingPlatform.Web.dll"]
