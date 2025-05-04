<#macro registrationLayout bodyClass="" displayMessage=true displayRequiredFields=false showAnotherWayIfPresent=true>
<!DOCTYPE html>
<html>
  <head>
    <title>Login Page</title>
    <link rel="stylesheet" href="resources/css/custom.css" />
    <meta charset="utf-8" />
  </head>
  <body class="${bodyClass}">
    <div class="kc-page">
      <#nested>
    </div>
  </body>
</html>
</#macro>
