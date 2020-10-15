FROM mcr.microsoft.com/dotnet/core/sdk:3.1

# File Author / Maintainer
MAINTAINER ANYWAYS BV

# copy api assemblies and files
RUN mkdir /var/app
RUN mkdir /var/app/data
RUN mkdir /var/app/source
RUN mkdir /var/app/target
RUN mkdir /var/app/logs

COPY ./bin/release/netcoreapp3.1/publish /var/app
COPY appsettings.Docker.json /var/app/appsettings.json

# couple data folder data volume.
VOLUME ["/var/app/data"]
VOLUME ["/var/app/source"]
VOLUME ["/var/app/target"]
VOLUME ["/var/app/logs"]

# Set the default command to execute when creating a new container
WORKDIR /var/app/
ENTRYPOINT dotnet ANYWAYS.VectorTiles.CycleNetworks.dll