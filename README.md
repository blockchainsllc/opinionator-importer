# Blockchain Voting - Importer

This importer connects via RPC to a etheterum client and pull data block-by-block into a mongodb

## Usage

1. You'll need a recent dotnet core runtime -or- the docker image
2. Build: 

    ```
    dotnet restore
    dotnet publish -c Release -o build
    ```

3. Run:

    ```
    dotnet build/VotingImporter.dll -r <http-rpc> -d <mongodb-url> -n <mongodb-name> -b<batchsize>
    ```


