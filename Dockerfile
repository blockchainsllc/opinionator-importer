# Build runtime image
FROM microsoft/dotnet:aspnetcore-runtime
WORKDIR /app
COPY build/ /app
ENTRYPOINT ["dotnet", "VotingImporter.dll"]