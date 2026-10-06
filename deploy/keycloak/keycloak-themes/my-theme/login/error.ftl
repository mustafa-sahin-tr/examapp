<#import "template.ftl" as layout>
<#-- Error page (#379): same card layout as login.ftl. The message is shown
     here as an alert, so the template's global alert is disabled. -->
<@layout.registrationLayout displayMessage=false; section>
  <#if section = "header">
    ${msg("errorTitle")}
  <#elseif section = "form">
      <div id="kc-error-message">
        <div class="auth-alert auth-alert--error" role="alert">
          <#if message?? && message.summary??>
            ${kcSanitize(message.summary)?no_esc}
          <#else>
            ${msg("errorTitle")}
          </#if>
        </div>
        <#if traceId??>
          <p class="instruction" id="traceId">${msg("traceIdSupportMessage", traceId)}</p>
        </#if>
      </div>

      <#if !skipLink??>
        <div class="auth-bottom-text">
          <#-- exam-client has no baseUrl configured (realm-export: ""), so
               fall back to the app's own login route, the same way
               login.ftl links to /app/register. -->
          <#if client?? && client.baseUrl?has_content>
            <a id="backToApplication" href="${client.baseUrl}">${kcSanitize(msg("backToApplication"))?no_esc}</a>
          <#else>
            <a id="backToApplication" href="/app/login">${kcSanitize(msg("backToLogin"))?no_esc}</a>
          </#if>
        </div>
      </#if>
  </#if>
</@layout.registrationLayout>
