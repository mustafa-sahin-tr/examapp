<#macro registrationLayout bodyClass="">
<!DOCTYPE html>
<html lang="${locale.currentLanguageTag}">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <link rel="stylesheet" href="${url.resourcesPath}/css/custom.css">
</head>
<body class="${bodyClass}">
    <#if realm.internationalizationEnabled && locale.supported?size gt 1>
      <div id="kc-locale">
        <div id="kc-locale-wrapper" class="kc-locale-wrapper">
          <div class="kc-locale-dropdown">
            <a href="#" id="kc-current-locale-link">${locale.current}</a>
            <ul>
              <#list locale.supported as l>
                <li class="kc-locale-item"><a href="${l.url}">${l.label}</a></li>
              </#list>
            </ul>
          </div>
        </div>
      </div>
    </#if>
    <#nested>
</body>
</html>
</#macro>
