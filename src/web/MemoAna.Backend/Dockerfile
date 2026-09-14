# Acesse https://aka.ms/customizecontainer para saber como personalizar seu contêiner de depuração e como o Visual Studio usa este Dockerfile para criar suas imagens para uma depuração mais rápida.

# Esta fase é usada durante a execução no VS no modo rápido (Padrão para a configuração de Depuração)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 5000
EXPOSE 5001


# Esta fase é usada para compilar o projeto de serviço
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/web/MemoAna.Backend/MemoAna.Backend.csproj", "src/web/MemoAna.Backend/"]
COPY ["src/web/MemoAna.Backend.Application/MemoAna.Backend.Application.csproj", "src/web/MemoAna.Backend.Application/"]
COPY ["src/web/MemoAna.Backend.Domain/MemoAna.Backend.Domain.csproj", "src/web/MemoAna.Backend.Domain/"]
COPY ["src/web/MemoAna.Backend.Composition/MemoAna.Backend.Composition.csproj", "src/web/MemoAna.Backend.Composition/"]
COPY ["src/web/MemoAna.Backend.Infrastructure/MemoAna.Backend.Infrastructure.csproj", "src/web/MemoAna.Backend.Infrastructure/"]
RUN dotnet restore "./src/web/MemoAna.Backend/MemoAna.Backend.csproj"
COPY . .
WORKDIR "/src/src/web/MemoAna.Backend"
RUN dotnet build "./MemoAna.Backend.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Esta fase é usada para publicar o projeto de serviço a ser copiado para a fase final
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MemoAna.Backend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Esta fase é usada na produção ou quando executada no VS no modo normal (padrão quando não está usando a configuração de Depuração)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER root 
RUN apt-get update && apt-get install -y unzip curl \
    && curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdout -v latest -l /vsdbg \
    && chmod -R +xr /vsdbg
USER $APP_UID
ENTRYPOINT ["dotnet", "MemoAna.Backend.dll"]