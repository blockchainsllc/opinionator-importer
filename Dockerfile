# Build runtime image
FROM microsoft/dotnet:aspnetcore-runtime
ARG CI_VERSION="v0.0.1"
ENV VERSION=$CI_VERSION
WORKDIR /app
COPY build/ /app
ENTRYPOINT ["dotnet", "VotingImporter.dll"]
