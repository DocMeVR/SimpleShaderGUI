# Changelog

## [1.2.1](https://github.com/DocMeVR/SimpleShaderGUI/compare/1.2.0...1.2.1) (2026-10-31)


### Bug Fixes

* open only web links from the inspector banner and link row ([0acc9ee](https://github.com/DocMeVR/SimpleShaderGUI/commit/0acc9eececcfa2ffc77033f17f209894145d5b0a))
* remove the bundled Assets/DocMe/ShaderUI copy when VCC installs the package ([9022286](https://github.com/DocMeVR/SimpleShaderGUI/commit/90222864d4af9c78a4e098f94a194d228599c4b7))

## [1.2.0](https://github.com/DocMeVR/SimpleShaderGUI/compare/1.1.0...1.2.0) (2026-10-18)


### Features

* add a [Dropdown] attribute for lists longer than seven entries ([58f9f2b](https://github.com/DocMeVR/SimpleShaderGUI/commit/58f9f2b31c059c0a48bd0cb52ad3a00da9caf945))
* hide the alpha of a colour with [NoAlpha] ([b5fff42](https://github.com/DocMeVR/SimpleShaderGUI/commit/b5fff4222b3a057de4100ed3a6598189ca765dae))
* **localization:** add a tool that keeps language files in step with the shader ([c13d4dc](https://github.com/DocMeVR/SimpleShaderGUI/commit/c13d4dc51d542385bc17dff916bf48262f75a9c7))
* **localization:** carry translated labels, tooltips, entries and section titles in the layout ([de0cff8](https://github.com/DocMeVR/SimpleShaderGUI/commit/de0cff86d018c936947a75a1a9f15f79d436e0f9))
* **localization:** find language files by name and resolve texts with fallbacks ([e695c27](https://github.com/DocMeVR/SimpleShaderGUI/commit/e695c275a47bde1ac2c869efffd0b6e26f08f317))
* **localization:** read and write gettext language files ([80cb32f](https://github.com/DocMeVR/SimpleShaderGUI/commit/80cb32fc4200d62860bd65f2f7c4eb7a18449c52))
* **localization:** share the inspector language with NDMF ([ea597b7](https://github.com/DocMeVR/SimpleShaderGUI/commit/ea597b7f304bec65834694dc032f955a02d180ae))
* **localization:** ship the inspector's own texts in Japanese ([6c863df](https://github.com/DocMeVR/SimpleShaderGUI/commit/6c863dfcc0ac7b8bd47d2175c14460cc24d005fd))
* **localization:** show the inspector in the chosen language with a Language switch ([fc580f2](https://github.com/DocMeVR/SimpleShaderGUI/commit/fc580f2f4fd348c54ab098e5ff356a19cc079f93))
* **localization:** translate the classic inspector and the fallback bake button ([2b37521](https://github.com/DocMeVR/SimpleShaderGUI/commit/2b37521f0744a00d6f0e0369c58f241f697d5567))


### Bug Fixes

* let a field row grow when its label wraps ([3406520](https://github.com/DocMeVR/SimpleShaderGUI/commit/340652021355a4be8988a2c6f761b18cad2160e4))
* let footer rows and the language switch grow with a wrapped label too ([d31da9c](https://github.com/DocMeVR/SimpleShaderGUI/commit/d31da9c91bb2b71c043ddde03ab0ee514fd6c65f))
* **localization:** draw Japanese with a TextCore font asset ([1ed5105](https://github.com/DocMeVR/SimpleShaderGUI/commit/1ed5105b218206bf3db622b81d6f0c2899b93b22))
* **localization:** find the changed-mark text and translate only the first header ([dfd12f9](https://github.com/DocMeVR/SimpleShaderGUI/commit/dfd12f9dc5c31c837f27827220122b0961d49df0))
* **localization:** never rewrite a language file the reader cannot read cleanly ([8c525a3](https://github.com/DocMeVR/SimpleShaderGUI/commit/8c525a344d52450782551a715875ca841053d29f))


### Performance Improvements

* **localization:** check the Config attribute cheaply in the Assets menu ([4afd656](https://github.com/DocMeVR/SimpleShaderGUI/commit/4afd656185d8eee1af0cf288dc23dcf76afc0994))

## [1.1.0](https://github.com/DocMeVR/SimpleShaderGUI/compare/1.0.0...1.1.0) (2026-10-12)


### Features

* bake a fallback picture for VRChat from the material ([eba8d7a](https://github.com/DocMeVR/SimpleShaderGUI/commit/eba8d7acf8785f57639de4dfba5e7d5825de1778))

## 1.0.0 (2026-10-10)


### Features

* add material inspector and config editor ([8840c60](https://github.com/DocMeVR/SimpleShaderGUI/commit/8840c6000ecee29ae2fd0a179055c6a49b0a6d9d))
* add material inspector and config editor ([4270fbb](https://github.com/DocMeVR/SimpleShaderGUI/commit/4270fbb7eb4596fd8c75a6ff1b6278e7f0c2a7a6))
* add property search and close foldables by default ([365c764](https://github.com/DocMeVR/SimpleShaderGUI/commit/365c7643a46598b13dc75f1f0fb43b9714350801))
* add section search prefix and configurable banner icon size ([888c92d](https://github.com/DocMeVR/SimpleShaderGUI/commit/888c92d0df020d109ad8c7c3b4f09b56fd34c38d))
* add shader UI config and section-name attribute ([03e8bed](https://github.com/DocMeVR/SimpleShaderGUI/commit/03e8bedb4f9b7668e6cdb441f2cc3d9f7684db2e))
* add shader UI config and section-name attribute ([6cbb599](https://github.com/DocMeVR/SimpleShaderGUI/commit/6cbb599bef97368cfb3c6e64d494d9fc38d058de))
* rebuild the material inspector with UI Toolkit ([d8323dd](https://github.com/DocMeVR/SimpleShaderGUI/commit/d8323ddadc1699a233094113824a99d86f8c9b1d))
* restyle banner links and improve search ([ee9272b](https://github.com/DocMeVR/SimpleShaderGUI/commit/ee9272b5732541e7dc860bd41afc01743ff58d43))


### Bug Fixes

* draw banner links in a single rect to prevent inspector blanking ([144ed1f](https://github.com/DocMeVR/SimpleShaderGUI/commit/144ed1f2c67bbec7266710ea7750484ddd9332ad))
* wrap banner links to fit inspector width ([603bad1](https://github.com/DocMeVR/SimpleShaderGUI/commit/603bad1f664dd9a155e43756021264fb8caf0f90))
* wrap banner links to fit inspector width ([74858a0](https://github.com/DocMeVR/SimpleShaderGUI/commit/74858a038ee1e7dc308bc6932a394f9b52d37188))
