;;; PERDE_YAZI_DEGISTIR.lsp
;;; Komut: PERDE_YAZI_DEGISTIR
;;; Secilen alandaki TEXT/MTEXT icinde:
;;;   9ø14 -> 8ø14 , 7ø14 -> 6ø14 , 18ø14 -> 16ø14 , 14ø14 -> 12ø14
;;; Not: Cizimlerde ø yerine baska karakter varsa (or. %%c, Unicode F8) asagidaki *cap-ayrac*
;;;      sembolunu degistirin.

(vl-load-com)

(defun PYD-subst-tumu (yeni-eski metin / eski yeni s)
  (setq eski (car yeni-eski))
  (setq yeni (cdr yeni-eski))
  (setq s metin)
  (while (vl-string-search eski s)
    (setq s (vl-string-subst yeni eski s))
  )
  s
)

(defun PYD-apply-kurallar (str / cap yeni eski-yeni)
  ;; Cizimde cap sembolu farkliysa burayi degistirin (or. "%%c")
  (setq cap "ø")
  (setq yeni str)
  (foreach eski-yeni
    (list
      (cons (strcat "18" cap "14") (strcat "16" cap "14"))
      (cons (strcat "14" cap "14") (strcat "12" cap "14"))
      (cons (strcat "9" cap "14")  (strcat "8" cap "14"))
      (cons (strcat "7" cap "14")  (strcat "6" cap "14"))
    )
    (setq yeni (PYD-subst-tumu eski-yeni yeni))
  )
  yeni
)

(defun PYD-entity-guncelle (ename / obj tip eski yeni ed al)
  (setq ed (entget ename))
  (setq tip (cdr (assoc 0 ed)))
  (cond
    ((= tip "TEXT")
     (setq eski (cdr (assoc 1 ed)))
     (setq yeni (PYD-apply-kurallar eski))
     (if (/= eski yeni)
       (progn
         (entmod (subst (cons 1 yeni) (assoc 1 ed) ed))
         T
       )
     )
    )
    ((= tip "MTEXT")
     (setq obj (vlax-ename->vla-object ename))
     (setq eski (vla-get-textstring obj))
     (setq yeni (PYD-apply-kurallar eski))
     (if (/= eski yeni)
       (progn
         (vla-put-textstring obj yeni)
         T
       )
     )
    )
    ((= tip "INSERT")
     ;; Sadece bu blok referansinin ATTRIB zinciri
     (setq al 0)
     (setq ename (entnext ename))
     (while (and ename (= "ATTRIB" (cdr (assoc 0 (entget ename)))))
       (if (PYD-entity-guncelle ename) (setq al (1+ al)))
       (setq ename (entnext ename))
     )
     (> al 0)
    )
    ((= tip "ATTRIB")
     (setq ed (entget ename))
     (setq eski (cdr (assoc 1 ed)))
     (setq yeni (PYD-apply-kurallar eski))
     (if (/= eski yeni)
       (progn
         (entmod (subst (cons 1 yeni) (assoc 1 ed) ed))
         T
       )
     )
    )
  )
)

(defun c:PERDE_YAZI_DEGISTIR (/ ss i n en sayac)
  (princ "\nPencere veya capraz pencere ile alani secin (TEXT, MTEXT, blok yazilari)... ")
  (setq ss (ssget '((0 . "TEXT,MTEXT,INSERT"))))
  (if (not ss)
    (princ "\nSecim yok.")
    (progn
      (setq sayac 0)
      (setq i 0)
      (setq n (sslength ss))
      (while (< i n)
        (setq en (ssname ss i))
        (if (PYD-entity-guncelle en) (setq sayac (1+ sayac)))
        (setq i (1+ i))
      )
      (princ (strcat "\nGuncellenen nesne sayisi: " (itoa sayac)))
    )
  )
  (princ)
)
