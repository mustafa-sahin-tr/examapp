<#import "template.ftl" as layout>
<#-- New password page (#379): reached from the reset-password e-mail link and
     on the first login after an admin reset (UPDATE_PASSWORD required action).
     Same card layout as login.ftl; field names and the logout-sessions /
     cancel-aia controls follow Keycloak's base page so the action handler
     keeps working unchanged. "Sign out from other devices" is pre-checked:
     after a password reset that is the safe default (#379 review). -->
<@layout.registrationLayout displayInfo=true displayMessage=!messagesPerField.existsError('password','password-confirm'); section>
  <#if section = "header">
    ${msg("updatePasswordTitle")}
  <#elseif section = "info">
    ${msg("updatePasswordSubtitle")}
  <#elseif section = "form">
      <form id="kc-passwd-update-form" action="${url.loginAction}" method="post" class="auth-form">

        <#-- Hidden, read-only account name so password managers store the
             new password against the right account (not submitted as a
             credential; Keycloak ignores the field). -->
        <#assign pmUsername = (auth.attemptedUsername)!((user.username)!'')>
        <#if pmUsername?has_content>
          <input type="text" id="username" name="username" value="${pmUsername}"
                 autocomplete="username" readonly hidden aria-hidden="true" tabindex="-1" />
        </#if>

        <div class="form-group">
          <label for="password-new">${msg("passwordNew")}</label>
          <input id="password-new" name="password-new" type="password"
                 class="form-control<#if messagesPerField.existsError('password')> form-control--invalid</#if>"
                 autofocus required autocomplete="new-password" dir="ltr"
                 <#if messagesPerField.existsError('password')>aria-invalid="true" aria-describedby="input-error-password"</#if> />
          <#if messagesPerField.existsError('password')>
            <span id="input-error-password" class="field-error" aria-live="polite">
              ${kcSanitize(messagesPerField.get('password'))?no_esc}
            </span>
          </#if>
        </div>

        <div class="form-group">
          <label for="password-confirm">${msg("passwordConfirm")}</label>
          <input id="password-confirm" name="password-confirm" type="password"
                 class="form-control<#if messagesPerField.existsError('password-confirm')> form-control--invalid</#if>"
                 required autocomplete="new-password" dir="ltr"
                 <#if messagesPerField.existsError('password-confirm')>aria-invalid="true" aria-describedby="input-error-password-confirm"</#if> />
          <#if messagesPerField.existsError('password-confirm')>
            <span id="input-error-password-confirm" class="field-error" aria-live="polite">
              ${kcSanitize(messagesPerField.get('password-confirm'))?no_esc}
            </span>
          </#if>
        </div>

        <div class="auth-options">
          <label class="remember-me">
            <input type="checkbox" id="logout-sessions" name="logout-sessions" value="on" checked>
            ${msg("logoutOtherSessions")}
          </label>
        </div>

        <div class="auth-submit">
          <button type="submit" name="login" class="default-btn">${msg("doSubmit")}</button>
          <#if isAppInitiatedAction??>
            <button type="submit" name="cancel-aia" value="true" class="secondary-btn">${msg("doCancel")}</button>
          </#if>
        </div>

      </form>
  </#if>
</@layout.registrationLayout>
