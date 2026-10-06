// CI-only Karma config (#416). Used via `ng test --karma-config=karma.ci.conf.js`;
// local `ng test` keeps Angular's default config. GitHub runners need --no-sandbox.
module.exports = function (config) {
  config.set({
    basePath: '',
    frameworks: ['jasmine', '@angular-devkit/build-angular'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('@angular-devkit/build-angular/plugins/karma'),
    ],
    reporters: ['progress'],
    browsers: ['ChromeHeadlessCI'],
    customLaunchers: {
      ChromeHeadlessCI: {
        base: 'ChromeHeadless',
        flags: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
      },
    },
    browserNoActivityTimeout: 120000,
    captureTimeout: 120000,
    restartOnFileChange: false,
    singleRun: true,
  });
};
