FROM mcr.microsoft.com/dotnet/runtime:5.0

RUN mkdir /var/app
RUN mkdir /var/app/data
RUN mkdir /var/app/logs
RUN mkdir /var/app/config

VOLUME ["/var/app/config"]
VOLUME ["/var/app/data"]
VOLUME ["/var/app/temp"]
VOLUME ["/var/app/logs"]

# SET base url
ENV ASPNETCORE_URLS http://+:5000

# Expose ports
EXPOSE 5000

COPY ./bin/release/net5.0/publish /var/app
COPY appsettings.Docker.json /var/app/appsettings.json

# Set the default command to execute when creating a new container
WORKDIR /var/app/
ENTRYPOINT dotnet ANYWAYS.VectorTiles.CycleNetworks.dll