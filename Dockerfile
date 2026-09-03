FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY App.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish --configuration Release --no-restore --output /output

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /output .

EXPOSE 3000
ENTRYPOINT ["dotnet", "App.dll", "--urls", "http://0.0.0.0:3000"]
