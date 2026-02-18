# Build and push ApiService
```
dotnet publish ImageProcessor.ApiService \
    --os linux --arch arm64 \
    /t:PublishContainer \
    -p ContainerRegistry=docker.io \
    -p ContainerRepository=hlakarki/imageprocessor-api \
    -p ContainerImageTag=latest
```  
    
# Build and push Worker
```
dotnet publish ImageProcessor.Worker \
--os linux --arch arm64 \
/t:PublishContainer \
-p ContainerRegistry=docker.io \
-p ContainerRepository=hlakarki/imageprocessor-worker \
-p ContainerImageTag=latest
```