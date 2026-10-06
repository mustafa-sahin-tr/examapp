<#--
  Shared shell for every page of the "my-theme" login theme (issue #379).

  The macro signature matches Keycloak's base template.ftl
  (bodyClass / displayInfo / displayMessage / displayRequiredFields) and the
  body is rendered through sections ("header", "info", "form"), like base.
  That matters for two reasons:
    1. Our own pages (login, login-reset-password, login-update-password,
       error) share one card layout instead of each page inventing its own
       markup (reset/update/error used to reference CSS classes that did not
       exist anywhere, so they rendered as unstyled plain forms).
    2. Pages we do NOT override (info.ftl, login-page-expired.ftl, OTP,
       register, ...) come from the parent theme, `import "template.ftl"` and
       resolve it to THIS file. The previous macro only accepted bodyClass,
       so a parent page passing displayMessage=... could not render at all.

  Section contract:
    header -> page title (rendered inside <h3>)
    info   -> short description under the title (only when displayInfo=true)
    form   -> page body (form, links, ...)
    show-username, socialProviders -> optional, used by parent-theme pages
-->
<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'tr'}">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta name="robots" content="noindex, nofollow">
    <title>${msg("loginTitle",(realm.displayName!''))}</title>
    <link rel="stylesheet" href="${url.resourcesPath}/css/custom.css">
    <#-- Theme-level scripts (theme.properties "scripts=") and per-page
         scripts Keycloak injects (e.g. WebAuthn), as in base template.ftl. -->
    <#if properties.scripts?has_content>
        <#list properties.scripts?split(' ') as script>
            <script src="${url.resourcesPath}/${script}" type="text/javascript"></script>
        </#list>
    </#if>
    <#if scripts??>
        <#list scripts as script>
            <script src="${script}" type="text/javascript"></script>
        </#list>
    </#if>
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

    <div class="auth-area">
      <div class="auth-inner">

        <div class="auth-image-col">
          <img src="${url.resourcesPath}/img/login.png" alt="${msg("loginImageAlt")}">
        </div>

        <div class="auth-form-col">
          <div class="auth-form-header">
            <h3><#nested "header"></h3>
            <#if displayInfo>
              <p><#nested "info"></p>
            </#if>
            <#if displayRequiredFields>
              <p class="required-hint"><span class="required" aria-hidden="true">*</span> ${msg("requiredFields")}</p>
            </#if>
          </div>

          <#-- Mid-flow pages (password step, OTP, WebAuthn, ...) show which
               account is signing in and let the user restart the flow —
               same condition as base template.ftl. Our title is kept; the
               username row goes under it instead of replacing it. -->
          <#if auth?has_content && auth.showUsername() && !auth.showResetCredentials()>
            <#nested "show-username">
            <div id="kc-username" class="auth-username">
              <span id="kc-attempted-username" class="auth-username__name">${auth.attemptedUsername}</span>
              <a id="reset-login" class="auth-username__restart" href="${url.loginRestartFlowUrl}">${msg("restartLoginTooltip")}</a>
            </div>
          </#if>

          <#-- Global (not field-bound) feedback: wrong credentials, the
               "email sent" info after a reset request, expired action
               token, ... Pages showing a field-level error pass
               displayMessage=false for that case to avoid a duplicate. -->
          <#if displayMessage && message?? && (message.type != 'warning' || !isAppInitiatedAction??)>
            <div class="auth-alert auth-alert--${message.type}" role="<#if message.type == 'error'>alert<#else>status</#if>">
              ${kcSanitize(message.summary)?no_esc}
            </div>
          </#if>

          <#nested "form">

          <#-- Alternative authenticator / organization switch / identity
               providers, as offered by base template.ftl to parent pages. -->
          <#if auth?has_content && auth.showTryAnotherWayLink()>
            <form id="kc-select-try-another-way-form" action="${url.loginAction}" method="post" class="auth-secondary-action">
              <input type="hidden" name="tryAnotherWay" value="on"/>
              <button type="submit" id="try-another-way" class="link-btn">${msg("doTryAnotherWay")}</button>
            </form>
          </#if>

          <#if switchOrganizationEnabled?? && switchOrganizationEnabled>
            <form id="kc-switch-organization-form" action="${url.loginAction}" method="post" class="auth-secondary-action">
              <input type="hidden" name="switchOrganization" value="true"/>
              <button type="submit" id="switch-organization" class="link-btn">${msg("doSwitchOrganization")}</button>
            </form>
          </#if>

          <#nested "socialProviders">
        </div>

      </div>
    </div>
</body>
</html>
</#macro>
