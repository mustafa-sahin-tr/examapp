<#import "template.ftl" as layout>
<@layout.registrationLayout>
  <link rel="stylesheet" href="${url.resourcesPath}/css/custom.css">

  <div class="login-container">
    <div class="login-card">
      <h2>${msg("updatePasswordTitle")}</h2>

      <form action="${url.loginAction}" method="post">
        <div class="form-field">
          <label for="password-new">${msg("passwordNew")}</label>
          <input id="password-new" name="password-new" type="password" required />
        </div>

        <div class="form-field">
          <label for="password-confirm">${msg("passwordConfirm")}</label>
          <input id="password-confirm" name="password-confirm" type="password" required />
        </div>

        <button class="login-button" type="submit">${msg("doSubmit")}</button>
      </form>

      <div class="actions">
        <a href="${url.loginUrl}">${msg("backToLogin")}</a>
      </div>
    </div>
  </div>
</@layout.registrationLayout>
