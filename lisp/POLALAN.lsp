;;; POLALAN.lsp
;;; Poligon icine tiklanan noktada, o kapali bolgenin alanini m2 cinsinden hesaplar
;;; ve "A=<alan>m²" biciminde TEXT yazar; komut satirina da yazar.
;;; Yukleme: APPLOAD ile bu dosyayi yukleyin. Komut: POLALAN
;;;
;;; Cizim birimleri: INSUNITS (mm/cm/m vb.) alan karesine yansitilir.
;;; INSUNITS=0 veya bilinmeyen: varsayim cm (cm^2 -> m^2).

(vl-load-com)

(defun polalan-insunits->m2-factor (/ ins)
  (setq ins (getvar "INSUNITS"))
  (cond
    ((= ins 1) 0.00064516)           ; inc^2 -> m^2
    ((= ins 2) 0.09290304)           ; ft^2 -> m^2
    ((= ins 4) 1e-6)                 ; mm^2 -> m^2
    ((= ins 5) 1e-4)                 ; cm^2 -> m^2
    ((= ins 6) 1.0)                  ; m^2
    ((= ins 7) 1e-6)                 ; km^2 (nadir)
    (T 1e-4)                         ; tanimli degil: cm varsay
  )
)

(defun polalan-ent-area (e / o a r)
  (if (and e (= (type e) 'ENAME))
    (progn
      (setq r (vl-catch-all-apply 'vlax-ename->vla-object (list e)))
      (if (vl-catch-all-error-p r)
        nil
        (progn
          (setq o r)
          (cond
            ((vlax-property-available-p o 'Area)
              (setq a (vl-catch-all-apply 'vlax-get (list o 'Area)))
              (if (vl-catch-all-error-p a) nil a))
            (T
              (setq a (vl-catch-all-apply 'vlax-curve-getArea (list o)))
              (if (vl-catch-all-error-p a) nil a))
          )
        )
      )
    )
    nil
  )
)

(defun polalan-variant-or-self (v)
  (if (= (type v) 'VARIANT) (vlax-variant-value v) v)
)

(defun polalan-sa-3 (v)
  (vlax-safearray->list (polalan-variant-or-self v))
)

(defun polalan-bbox-pair (o / mn mx ok)
  (setq ok (vl-catch-all-apply
             (function (lambda () (vla-getboundingbox o 'mn 'mx) T))
             nil))
  (if (vl-catch-all-error-p ok)
    nil
    (list (polalan-sa-3 mn) (polalan-sa-3 mx))
  )
)

(defun polalan-curve-closed-p (e o / ed flags typ c)
  (setq ed (entget e) typ (cdr (assoc 0 ed)))
  (cond
    ((= typ "LWPOLYLINE")
      (= (logand (cdr (assoc 70 ed)) 1) 1))
    ((= typ "POLYLINE")
      (= (logand (cdr (assoc 70 ed)) 1) 1))
    ((= typ "ELLIPSE") T)
    ((vlax-property-available-p o 'Closed)
      (progn
        (setq c (vl-catch-all-apply 'vlax-get (list o 'Closed)))
        (if (vl-catch-all-error-p c)
          nil
          (or (= c :vlax-true) (= c T) (= c 1)))))
    (T nil)
  )
)

(defun polalan-sample-xy (e steps / o sp ep n i pts p)
  (setq o (vl-catch-all-apply 'vlax-ename->vla-object (list e)))
  (if (vl-catch-all-error-p o)
    nil
    (progn
      (setq sp (vl-catch-all-apply 'vlax-curve-getStartParam (list o)))
      (setq ep (vl-catch-all-apply 'vlax-curve-getEndParam (list o)))
      (if (or (vl-catch-all-error-p sp) (vl-catch-all-error-p ep))
        nil
        (progn
          (setq n (max (fix steps) 8) pts nil i 0)
          (repeat (1+ n)
            (setq p (vl-catch-all-apply 'vlax-curve-getPointAtParam
                      (list o (+ sp (* i (/ (- ep sp) 1.0 n))))))
            (if (vl-catch-all-error-p p)
              (setq pts nil)
              (setq pts (cons (list (car p) (cadr p)) pts)))
            (setq i (1+ i))
          )
          (if pts (reverse pts) nil)
        )
      )
    )
  )
)

;;; Iki boyutlu ic testi (isin); verts = ((x y) ...)
(defun polalan-pt-in-poly (x y verts / inside n i j xi yi xj yj)
  (if (or (null verts) (< (length verts) 3))
    nil
    (progn
      (setq n (length verts) inside nil j (1- n) i 0)
      (repeat n
        (setq xi (car (nth i verts)) yi (cadr (nth i verts))
              xj (car (nth j verts)) yj (cadr (nth j verts)))
        (if (/= (> yi y) (> yj y))
          (if (< x (+ xi (* (/ (- xj xi) (- yj yi)) (- y yi))))
            (setq inside (not inside))))
        (setq j i i (1+ i))
      )
      inside
    )
  )
)

(defun polalan-circle-contains (pt e / c r)
  (setq c (cdr (assoc 10 (entget e)))
        r (cdr (assoc 40 (entget e))))
  (<= (distance pt c) r)
)

(defun polalan-hatch-bbox-contains (pt e / o box ll ur)
  (setq o (vl-catch-all-apply 'vlax-ename->vla-object (list e)))
  (if (vl-catch-all-error-p o)
    nil
    (progn
      (setq box (polalan-bbox-pair o))
      (if (null box)
        nil
        (progn
          (setq ll (car box) ur (cadr box))
          (and (<= (car ll) (car pt) (car ur))
               (<= (cadr ll) (cadr pt) (cadr ur)))
        )
      )
    )
  )
)

(defun polalan-entity-contains-pt (pt e / o ed typ verts)
  (setq ed (entget e) typ (cdr (assoc 0 ed)))
  (cond
    ((= typ "CIRCLE") (polalan-circle-contains pt e))
    ((member typ '("LWPOLYLINE" "POLYLINE" "ELLIPSE" "SPLINE"))
      (setq o (vl-catch-all-apply 'vlax-ename->vla-object (list e)))
      (if (vl-catch-all-error-p o)
        nil
        (if (not (polalan-curve-closed-p e o))
          nil
          (progn
            (setq verts (polalan-sample-xy e 96))
            (and verts (polalan-pt-in-poly (car pt) (cadr pt) verts))
          )
        )
      )
    )
    ((= typ "HATCH") (polalan-hatch-bbox-contains pt e))
    (T nil)
  )
)

(defun polalan-ss-near (pt half / p1 p2 f)
  (setq p1 (list (- (car pt) half) (- (cadr pt) half) 0.0)
        p2 (list (+ (car pt) half) (+ (cadr pt) half) 0.0))
  (setq f (list (cons 0 "LWPOLYLINE,POLYLINE,CIRCLE,ELLIPSE,SPLINE,HATCH")))
  (ssget "_C" p1 p2 f)
)

(defun polalan-smallest-containing (pt / d dmax ss i e a best besta)
  (setq d (max (/ (getvar "VIEWSIZE") 40.0) 1.0)
        dmax (* (getvar "VIEWSIZE") 4.0)
        ss nil)
  (while (and (< d dmax) (null ss))
    (setq ss (polalan-ss-near pt d))
    (if (null ss) (setq d (* d 2.0)))
  )
  (if (null ss)
    nil
    (progn
      (setq i 0 best nil besta nil)
      (repeat (sslength ss)
        (setq e (ssname ss i))
        (if (polalan-entity-contains-pt pt e)
          (progn
            (setq a (polalan-ent-area e))
            (if (and a (> a 0.0)
                     (or (null besta) (< a besta)))
              (setq best e besta a))))
        (setq i (1+ i))
      )
      best
    )
  )
)

(defun polalan-put-text (pt s / th h)
  (setq th (getvar "TEXTSIZE"))
  (if (or (null th) (<= th 0.0)) (setq th 2.5))
  (setq h th)
  (entmake
    (list
      '(0 . "TEXT")
      '(100 . "AcDbEntity")
      (cons 8 (getvar "CLAYER"))
      '(100 . "AcDbText")
      (cons 10 pt)
      (cons 40 h)
      (cons 1 s)
      (cons 50 0.0)
      (cons 41 1.0)
      (cons 7 (getvar "TEXTSTYLE"))
      '(71 . 0)
      '(72 . 0)
      (cons 11 pt)
    )
  )
)

(defun C:POLALAN (/ pt e fac a2 s old_os old_ce)
  (setq old_os (getvar "OSMODE") old_ce (getvar "CMDECHO"))
  (setvar "OSMODE" 0)
  (setvar "CMDECHO" 0)
  (setq pt (getpoint "\nPoligon icinde bir nokta secin: "))
  (if pt
    (progn
      (setq e (polalan-smallest-containing pt))
      (if (null e)
        (progn
          (prompt "\nBu noktada uygun kapali bolge bulunamadi. Kenardan nesne secmeyi deneyin.")
          (setq e (car (entsel "\nKapali LWPOLYLINE / CIRCLE / HATCH secin: ")))
        )
      )
      (if e
        (progn
          (setq fac (polalan-insunits->m2-factor))
          (setq a2 (* (polalan-ent-area e) fac))
          (if (or (null a2) (<= a2 0.0))
            (prompt "\nAlan okunamadi (acik egri veya desteklenmeyen nesne).")
            (progn
              (setq s (strcat "A=" (rtos a2 2 4) "m²"))
              (prompt (strcat "\n" s))
              (polalan-put-text pt s)
            )
          )
        )
        (prompt "\nIptal.")
      )
    )
    (prompt "\nIptal.")
  )
  (setvar "OSMODE" old_os)
  (setvar "CMDECHO" old_ce)
  (princ)
)

(princ "\nPOLALAN yuklendi. Komut: POLALAN")
(princ)
