<#import "template.ftl" as layout>
<@layout.registrationLayout>
  <link rel="stylesheet" href="${url.resourcesPath}/css/custom.css">

  <div class="login-container">
    <div class="login-card">
      <h2>Yeni Şifre Belirle</h2>

      <form action="${url.loginAction}" method="post">
        <div class="form-field">
          <label for="password-new">Yeni Şifre</label>
          <input id="password-new" name="password-new" type="password" required />
        </div>

        <div class="form-field">
          <label for="password-confirm">Yeni Şifre (Tekrar)</label>
          <input id="password-confirm" name="password-confirm" type="password" required />
        </div>

        <button class="login-button" type="submit">Şifreyi Güncelle</button>
      </form>

      <div class="actions">
        <a href="${url.loginUrl}">Giriş Sayfasına Dön</a>
      </div>
    </div>
  </div>
</@layout.registrationLayout>
