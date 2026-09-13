<#import "template.ftl" as layout>
<@layout.registrationLayout>
  <div class="error">
    <#if message?? && message.summary??>
      ${kcSanitize(message.summary)?no_esc}
    <#else>
      ${msg("errorTitle")}
    </#if>
  </div>
  <div class="actions">
    <a href="${url.loginUrl}">${msg("backToApplication")}</a>
  </div>
</@layout.registrationLayout>
