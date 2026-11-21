# ui-font

构建时会被 `build.ps1` 编进 exe 的界面字体，程序不依赖系统已安装字体。

- `DSH-Sans.ttf`、`DSH-Sans-Bold.ttf`
  - 来源：Noto Sans SC（字形源自 Source Han Sans），版权声明 `© 2014-2021 Adobe`，采用 **SIL Open Font License 1.1**，全文见 `LICENSE.txt`。
  - 处理：可变字体分别实例化为 wght 400 / 700 两档静态字重；按「源码中出现的全部字符 + GB2312 汉字 + 常用符号」裁成 6913 字符的子集；去掉 hinting。
  - 族名改为 `DSH Sans` / `DSH Sans Bold`，以避开原字体的保留字体名（OFL 第 3 条）。
  - 每个字重约 2.0 MB，因此 exe 体积比纯系统字体版本大约多 4 MB。

重新生成字体时会用到 `fonttools`：

```
python -m pip install fonttools brotli
```
