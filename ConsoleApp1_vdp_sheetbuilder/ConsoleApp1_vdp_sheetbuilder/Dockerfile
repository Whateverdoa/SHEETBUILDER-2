# Use the official .NET 8 runtime image as the base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Use the official .NET 8 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY ["ConsoleApp1_vdp_sheetbuilder.csproj", "./"]
RUN dotnet restore "ConsoleApp1_vdp_sheetbuilder.csproj"

# Copy the rest of the source code
COPY . .
WORKDIR "/src/."

# Build the application
RUN dotnet build "ConsoleApp1_vdp_sheetbuilder.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "ConsoleApp1_vdp_sheetbuilder.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage/image
FROM base AS final
WORKDIR /app

# Create uploads directory
RUN mkdir -p /app/wwwroot/uploads

# Copy the published application
COPY --from=publish /app/publish .

# Set permissions for uploads directory
RUN chmod 755 /app/wwwroot/uploads

# Expose port 80
EXPOSE 80

# Set the entry point
ENTRYPOINT ["dotnet", "ConsoleApp1_vdp_sheetbuilder.dll"]