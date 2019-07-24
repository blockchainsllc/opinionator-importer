# Build runtime image
FROM microsoft/dotnet:aspnetcore-runtime
ENV VERSION=${CI_VERSION}
WORKDIR /app
COPY build/ /app
ENTRYPOINT ["dotnet", "VotingImporter.dll"]
