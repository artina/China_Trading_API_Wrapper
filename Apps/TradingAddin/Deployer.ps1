if ($args[0] -ne "Release") { Exit }

$SOURCE_DIR = "C:\Dev\repos\Trading\_bin\Release"
$DEPLOY_DIR = "C:\Dev\repos\Trading\_bin\_deploy"

Remove-Item -Path "$($DEPLOY_DIR)\*" -Recurse -Force
Copy-Item "$($SOURCE_DIR)\*" -Destination $DEPLOY_DIR

Remove-Item -Path "$($DEPLOY_DIR)\*.exp"
Remove-Item -Path "$($DEPLOY_DIR)\*.lib"
Remove-Item -Path "$($DEPLOY_DIR)\*.pdb"
Remove-Item -Path "$($DEPLOY_DIR)\*.iobj"
Remove-Item -Path "$($DEPLOY_DIR)\*.ipdb"
Remove-Item -Path "$($DEPLOY_DIR)\*.ipdb"

Remove-Item -Path "$($DEPLOY_DIR)\Test*"
Remove-Item -Path "$($DEPLOY_DIR)\*packed*"
Remove-Item -Path "$($DEPLOY_DIR)\*64*"

Remove-Item -Path "$($DEPLOY_DIR)\TradingAddin.dll.config"
