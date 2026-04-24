@echo off
start /high valheim_server.exe -nographics -batchmode -name {settings.serverName:quote} -port {settings.port} -world {settings.worldName:quote} -password {settings.password:quote} -public {settings.public:boolint} {settings.crossplay:flag:-crossplay} {settings.additionalArgs}
