rem Make sure dotnet-ef is installed: dotnet tool install --global dotnet-ef

dotnet ef migrations remove --context SQLServerDataContext
dotnet ef migrations remove --context SQLiteDataContext
dotnet ef migrations remove --context PostgreSQLDataContext

rem TODO: add for other DB types