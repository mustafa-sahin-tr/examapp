<#import "template.ftl" as layout>
<@layout.registrationLayout>
  <link rel="stylesheet" href="${url.resourcesPath}/css/custom.css">

  <div class="login-container">
    <div class="login-card">
      <h2>${msg("emailForgotTitle")}</h2>

      <form action="${url.loginAction}" method="post">
        <div class="form-field">
          <label for="username">${msg("email")}</label>
          <input id="username" name="username" type="text" autofocus required />
        </div>

        <button class="login-button" type="submit">${msg("doSubmit")}</button>
      </form>

      <div class="actions">
        <a href="${url.loginUrl}">${msg("backToLogin")}</a>
      </div>
    </div>
  </div>
</@layout.registrationLayout>
