# Use the official .NET SDK image as the base image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set the working directory inside the container
WORKDIR /app

# Run dotnet new to create a new API project
# RUN dotnet new webapi -n BooksApi -o . 

# Expose the default ASP.NET Core port for development
RUN apt-get update && apt-get install -y ca-certificates

# Install required tools for development (e.g., curl, nano, git)
RUN apt-get update \
    && apt-get install -y --no-install-recommends \        
        curl \
        nano \
        git \
        net-tools \
    && rm -rf /var/lib/apt/lists/*

RUN dotnet tool install -g microsoft.sqlpackage

# RUN cat << \EOF >> ~/.bashrc  \
#     # Add .NET Core SDK tools
#     export PATH="$PATH:/root/.dotnet/tools" \
#     EOF


# Set the ASP.NET Core environment to Development
ENV ASPNETCORE_ENVIRONMENT Development

# Set the ASP.NET Core application URL
ENV ASPNETCORE_URLS http://+:8005

# Create a new .NET Core WebAPI project
# RUN dotnet new webapi -n BooksApi

# WORKDIR /app/BooksApi

EXPOSE 8004

# Set up the entry point for Visual Studio Code remote development
# ENTRYPOINT ["dotnet", "watch", "run", "--urls", "http://0.0.0.0:8002"]
CMD ["sleep", "infinity"]
