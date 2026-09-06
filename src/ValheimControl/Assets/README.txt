Place a valheim.ico file here to give the compiled .exe and its window a
custom icon. The .csproj references Assets\valheim.ico as the
ApplicationIcon - if the file is missing, remove or comment out that line
in ValheimControl.csproj before building, or the build will fail looking
for it.
