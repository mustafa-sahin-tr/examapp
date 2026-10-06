<#import "template.ftl" as layout>
<#-- backToLogin/backToApplication go through kcSanitize: Keycloak 24 TR bundle stores them as "&laquo; ...", 26 as a literal «. -->
<#-- "Şifremi unuttum" (#379): same card layout as login.ftl via template.ftl.
     A field-level username error is shown under the input, so the global
     alert is suppressed in that case (same rule as Keycloak's base page). -->
<@layout.registrationLayout displayInfo=true displayMessage=!messagesPerField.existsError('username'); section>
  <#if section = "header">
    ${msg("emailForgotTitle")}
  <#elseif section = "info">
    <#if realm.duplicateEmailsAllowed>
      ${msg("emailInstructionUsername")}
    <#else>
      ${msg("emailInstruction")}
    </#if>
  <#elseif section = "form">
      <form id="kc-reset-password-form" action="${url.loginAction}" method="post" class="auth-form">

        <div class="form-group">
          <label for="username"><#if !realm.loginWithEmailAllowed>${msg("username")}<#elseif !realm.registrationEmailAsUsername>${msg("usernameOrEmail")}<#else>${msg("email")}</#if></label>
          <input id="username" name="username" type="text"
                 class="form-control<#if messagesPerField.existsError('username')> form-control--invalid</#if>"
                 <#if realm.loginWithEmailAllowed>placeholder="${msg("usernameOrEmailPlaceholder")}"</#if>
                 value="${(auth.attemptedUsername)!''}"
                 autofocus required autocomplete="username" dir="ltr"
                 <#if messagesPerField.existsError('username')>aria-invalid="true" aria-describedby="input-error-username"</#if> />
          <#if messagesPerField.existsError('username')>
            <span id="input-error-username" class="field-error" aria-live="polite">
              ${kcSanitize(messagesPerField.get('username'))?no_esc}
            </span>
          </#if>
        </div>

        <div class="auth-submit">
          <button type="submit" class="default-btn">${msg("doSubmit")}</button>
        </div>

      </form>

      <div class="auth-bottom-text">
        <a href="${url.loginUrl}">${kcSanitize(msg("backToLogin"))?no_esc}</a>
      </div>
  </#if>
</@layout.registrationLayout>
