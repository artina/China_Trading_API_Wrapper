$SWIG = "$($args[0])packages\swigwintools.3.0.12\tools\swigwin-3.0.12\swig.exe"
$INCLUDE = "$($args[0])3rdparty\esunny-api\domestic-trade\include"
$PROJ_DIR = "$($args[0])API\ESunny\DomesticTrade"
$PROJ_DIR_CPP = "$PROJ_DIR\generated\cpp"
$PROJ_DIR_CS = "$PROJ_DIR\generated\csharp"
$CPP_WRAPPER = "ESunnyDomesticTradeCppWrapper"
$NAMESPACE = "ESunny.Domestic.Trade"

# Create folders if not exist
New-Item -ItemType Directory -Path $PROJ_DIR_CPP -Force
New-Item -ItemType Directory -Path $PROJ_DIR_CS -Force

# Clean up files
del $PROJ_DIR_CPP\*.h
del $PROJ_DIR_CPP\*.cpp
del $PROJ_DIR_CS\*.cs

# Run SWIG to generate wrappers
& "$SWIG" -csharp -c++ "-I$INCLUDE" -oh $PROJ_DIR_CPP\APIWrapper.h -o $PROJ_DIR_CPP\APIWrapper.cpp  -outdir $PROJ_DIR_CS -dllimport $CPP_WRAPPER -namespace $NAMESPACE $PROJ_DIR\SwigInterface.i

# Add calling convetion for callback class
(Get-Content $PROJ_DIR_CPP\APIWrapper.h).replace('virtual void', 'virtual void TAP_CDECL') | Set-Content $PROJ_DIR_CPP\APIWrapper.h
