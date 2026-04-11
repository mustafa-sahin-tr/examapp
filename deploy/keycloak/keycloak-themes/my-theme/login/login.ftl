<#import "template.ftl" as layout>
<@layout.registrationLayout>

<div class="auth-area">
  <div class="auth-inner">

    <div class="auth-image-col">
      <img src="${url.resourcesPath}/img/login.png" alt="Giriş">
    </div>

    <div class="auth-form-col">
      <div class="auth-form-header">
        <h3>Tekrar Hoş Geldin!</h3>
        <p>Hesabına giriş yap ve öğrenmeye devam et.</p>
      </div>

      <#if message?? && message.type == "error">
        <div class="auth-alert auth-alert--error">${message.summary}</div>
      </#if>

      <form action="${url.loginAction}" method="post" class="auth-form">

        <div class="form-group">
          <label for="username">E-posta veya Kullanıcı Adı</label>
          <input id="username" name="username" type="text"
                 placeholder="siz@ornek.com"
                 class="form-control"
                 value="${(login.username)!''}"
                 autofocus autocomplete="username" />
        </div>  

        <div class="form-group">
          <label for="password">Şifre</label>
          <input id="password" name="password" type="password"
                 placeholder="Şifreniz"
                 class="form-control"
                 autocomplete="current-password" />
        </div>

        <div class="auth-options">
          <#if realm.rememberMe>
            <label class="remember-me">
              <input type="checkbox" name="rememberMe" <#if login.rememberMe??>checked</#if>> Beni Hatırla
            </label>
          </#if>
          <a href="${url.loginResetCredentialsUrl}" class="forgot-link">Şifremi Unuttum</a>
        </div>

        <div class="auth-submit">
          <button type="submit" class="default-btn">
            Giriş Yap
            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="14" viewBox="0 0 18 14" fill="none">
              <path opacity="0.5" d="M16.25 6.75V7.25H1.25V6.75H16.25Z" fill="white" stroke="white"></path>
              <path d="M10.75 1L16.75 7L10.75 13" stroke="white" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"></path>
            </svg>
          </button>
        </div>

      </form>

      <#if social.providers?? && social.providers?size gt 0>
        <div class="social-divider"><span>veya</span></div>
        <div class="social-login">
          <#list social.providers as idp>
            <#if idp.alias == "google">
              <a class="gsi-material-button" href="${idp.loginUrl}">
                <div class="gsi-material-button-state"></div>
                <div class="gsi-material-button-content-wrapper">
                  <div class="gsi-material-button-icon">
                    <svg version="1.1" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" style="display:block;">
                      <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"/>
                      <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"/>
                      <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"/>
                      <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"/>
                    </svg>
                  </div>
                  <span class="gsi-material-button-contents">Google ile Giriş Yap</span>
                </div>
              </a>
            </#if>
          </#list>
        </div>
      </#if>

      <div class="auth-bottom-text">
        <span>Hesabın yok mu? <a href="/app/register">Kayıt Ol</a></span>
      </div>

    </div>
  </div>
</div>

</@layout.registrationLayout>
