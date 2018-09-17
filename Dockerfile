# Build runtime image
FROM microsoft/dotnet:aspnetcore-runtime
WORKDIR /app
COPY build/ /app/out
ENTRYPOINT ["dotnet", "VotingImporter.dll"]