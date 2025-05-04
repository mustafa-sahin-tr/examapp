<#import "template.ftl" as layout>
<@layout.registrationLayout>
  <link rel="stylesheet" href="${url.resourcesPath}/css/custom.css">

  <div class="login-container">
    <div class="login-card">
      <h2>Giriş Yap</h2>

      <form action="${url.loginAction}" method="post">
        <div class="form-field">
          <label for="username">Email</label>
          <input id="username" name="username" type="text" autofocus required />
        </div>

        <div class="form-field">
          <label for="password">Şifre</label>
          <input id="password" name="password" type="password" required />
        </div>

        <button class="login-button" type="submit">Giriş Yap</button>
      </form>

      <div class="actions">
        <a href="${url.registrationUrl}">Kayıt Ol</a>
      </div>
    </div>
  </div>
</@layout.registrationLayout>
