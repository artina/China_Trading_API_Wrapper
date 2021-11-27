$API_NOTIFY_FILE = (Get-ChildItem -Path "$($args[0])generated\csharp" -Filter "*Notify.cs").Fullname
$TARGET_FILE = "$($args[0])generated\csharp\EventCallback.cs"

$NAMESPACE_REG_EXP = "(namespace.*) {"
$CLASS_REG_EXP = "public class (.*) :"
$METHOD_REG_EXP = "public virtual void (On.*)\((.*)\)"

foreach($line in Get-Content $API_NOTIFY_FILE) {
    if($line -match $NAMESPACE_REG_EXP) {
        Add-Content $TARGET_FILE "$($matches[1])"
        Add-Content $TARGET_FILE "{"
    }
}

foreach($line in Get-Content $API_NOTIFY_FILE) {
    if($line -match $CLASS_REG_EXP) {
        Add-Content $TARGET_FILE "public class EventCallback : $($matches[1])"
        Add-Content $TARGET_FILE "{`r`n"
    }
}

foreach($line in Get-Content $API_NOTIFY_FILE) {
    if($line -match $METHOD_REG_EXP) {

    $delegate_line = $matches[0].replace('virtual', 'delegate')
    $delegate_line = $delegate_line.replace('(', 'Handler(')
    Add-Content $TARGET_FILE "  $delegate_line;"

    $event_line = "  public event $($matches[1])Handler $($matches[1])Event;"
    Add-Content $TARGET_FILE $event_line

    $method_line = "  $($matches[0])"
    $method_line = $method_line.replace('virtual', 'override')
    Add-Content $TARGET_FILE $method_line
    Add-Content $TARGET_FILE "  {"

    $args = $matches[2].trim() -split '[\s|,]+'
    $args_no_type = @("") * ($args.length / 2)
    for ($i = 0; $i -lt $args_no_type.length; $i++) {
        $args_no_type[$i] = $args[2* $i + 1]
    }

    $args_joined = $args_no_type -join ", "
    $body_line = "    $($matches[1])Event?.Invoke($($args_joined));"
    Add-Content $TARGET_FILE $body_line
    Add-Content $TARGET_FILE "  }`r`n"

    }
}

Add-Content $TARGET_FILE "}`r`n}"

