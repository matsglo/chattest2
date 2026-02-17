module.exports = {
  "/api": {
    target: process.env["services__api__http__0"] || "http://localhost:5000",
    secure: false,
    changeOrigin: true,
    logLevel: "debug"
  }
};
