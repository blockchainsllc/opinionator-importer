dotnet publish -c release -r linux-x64
rsync -avH bin/Release/netcoreapp2.1/linux-x64/publish/. bran-stark:dotnet-importer


